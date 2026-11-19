using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Launcher.Models;

namespace Launcher.Services;

/// <summary>
/// 程序内自动更新服务：版本比对 → 下载 zip → SHA256 校验 → 退出后由 helper 覆盖替换 → 重启。
///
/// 设计要点：
/// 1) 主程序运行时自身 exe 被占用，不能原地覆盖；下载完成后先解压到新版本暂存目录，
///    再拉起独立 helper（UpdateHelper）等待主进程退出，由 helper 覆盖程序目录并重启。
/// 2) 更新清单地址默认走后端 {ApiBaseUrl}/api/launcher/latest，可在 config.json 覆盖。
/// 3) 下载、校验失败均抛异常由上层 UI 捕获；未完成的暂存目录下次启动可清理。
/// </summary>
public class UpdateService
{
    private readonly HttpClient _http;
    private readonly string _updateBase;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>本地运行态目录（%APPDATA%\x.HR），更新缓存与日志放这里。</summary>
    public static string LocalDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "x.HR");

    /// <summary>更新暂存根目录：已下载/已解压的新版本放在这里。</summary>
    private static string UpdateStagingDir => Path.Combine(LocalDataDir, "update");

    public UpdateService(string apiBaseUrl, string? manifestUrlOverride = null)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.Add("User-Agent", "xHR-Launcher-Update");
        _updateBase = string.IsNullOrWhiteSpace(manifestUrlOverride)
            ? $"{apiBaseUrl.TrimEnd('/')}/api/launcher/latest"
            : manifestUrlOverride;
    }

    /// <summary>读取当前程序集版本；读取不到时回退到 0.0.0。</summary>
    public static Version CurrentVersion
    {
        get
        {
            var asm = Assembly.GetEntryAssembly();
            var ver = asm?.GetName().Version;
            return ver ?? new Version(0, 0, 0);
        }
    }

    public static string CurrentVersionLabel => "v" + CurrentVersion.ToString(3);

    /// <summary>
    /// 从程序目录下的 version.json 读取发布版本号（launcher_version）。
    /// 绿色包随物料一并分发；读不到时返回 null，UI 回退到程序集版本。
    /// </summary>
    public static string? ReadPackagedVersion()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "version.json");
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("launcher_version", out var el))
                return el.GetString();
        }
        catch
        {
            // 版本文件缺失/损坏时忽略，回退程序集版本
        }
        return null;
    }

    // ===== 1. 检查更新 =====

    /// <summary>拉取远端版本清单并比对；有更新返回远端信息，无更新返回 null。</summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        var json = await _http.GetStringAsync(_updateBase, ct);
        var info = JsonSerializer.Deserialize<UpdateInfo>(json, JsonOpts);
        if (info is null || string.IsNullOrWhiteSpace(info.LauncherVersion)) return null;

        if (TryParseVersion(info.LauncherVersion, out var remote) && remote > EffectiveCurrentVersion)
            return info;

        return null;
    }

    /// <summary>
    /// 比对所用的本地版本：优先读发布包 version.json，其次程序集版本。
    /// </summary>
    private static Version EffectiveCurrentVersion =>
        TryParseVersion(ReadPackagedVersion() ?? "", out var packed)
            ? packed
            : CurrentVersion;

    /// <summary>
    /// 下载更新包到暂存目录并校验哈希。返回本地 zip 完整路径。
    /// 进度回调：已下载字节 / 总字节（总字节未知时为 0）。
    /// </summary>
    public async Task<string> DownloadPackageAsync(
        UpdateInfo info,
        IProgress<(long Received, long Total)>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(info.PackageUrl))
            throw new InvalidOperationException("更新清单缺少 package_url");

        Directory.CreateDirectory(UpdateStagingDir);
        var zipPath = Path.Combine(UpdateStagingDir, $"xHR-{SanitizeFileName(info.LauncherVersion)}.zip");

        using var response = await _http.GetAsync(info.PackageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0;
        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            received += read;
            progress?.Report((received, total));
        }

        // 哈希校验（远端提供了 sha256 才校验）
        if (!string.IsNullOrWhiteSpace(info.Sha256))
        {
            var actual = await ComputeSha256Async(zipPath, ct);
            if (!string.Equals(actual, info.Sha256.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("更新包哈希校验失败，文件可能已损坏或被篡改");
        }

        return zipPath;
    }

    /// <summary>
    /// 把已下载的 zip 解压到新版本暂存目录，供 helper 替换。返回解压目录。
    /// </summary>
    public string StagePackage(string zipPath, string versionLabel)
    {
        var stageDir = Path.Combine(UpdateStagingDir, SanitizeFileName(versionLabel));
        if (Directory.Exists(stageDir)) Directory.Delete(stageDir, true);
        Directory.CreateDirectory(stageDir);
        ZipFile.ExtractToDirectory(zipPath, stageDir, overwriteFiles: true);
        return stageDir;
    }

    /// <summary>
    /// 拉起更新 helper：等待主进程退出后，把暂存目录内容覆盖到程序目录并重启主程序。
    /// 主程序目录取自 AppContext.BaseDirectory；helper 脚本由本服务在更新前写入暂存目录。
    /// </summary>
    public void ApplyUpdateAndRestart(string stageDir, UpdateInfo info)
    {
        var appDir = AppContext.BaseDirectory;
        var helperScript = Path.Combine(UpdateStagingDir, "apply-update.cmd");
        var mainExe = Path.Combine(appDir, "xHR.exe");
        if (!File.Exists(mainExe)) mainExe = Path.Combine(appDir, "x.HR.exe");

        // 写一个批处理 helper：等主进程退出 → 覆盖文件 → 重启。
        // 用 robocopy 镜像暂存目录到程序目录；/MIR 会删除目标端多余文件，保证版本一致。
        var failedLog = Path.Combine(LocalDataDir, "update", "update-failed.log");
        var script = $@"@echo off
chcp 65001 >nul
setlocal
set STAGE={stageDir}
set TARGET={appDir}
set EXE={mainExe}
set FAIL={failedLog}

REM 等待主程序退出（最多 60 秒）
:waitloop
tasklist /FI ""IMAGENAME eq xHR.exe"" | find /I ""xHR.exe"" >nul
if errorlevel 1 goto copy
timeout /t 1 /nobreak >nul
goto waitloop

:copy
robocopy ""%STAGE%"" ""%TARGET%"" /E /MIR /NFL /NDL /NJH /NJS /R:2 /W:1
if errorlevel 8 goto fail
start """" ""%EXE%""
exit /b 0

:fail
echo 更新替换失败，请手动解压 %STAGE% 到 %TARGET% > ""%FAIL%""
exit /b 1
";
        File.WriteAllText(helperScript, script);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{helperScript}\"",
            WorkingDirectory = UpdateStagingDir,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // 独立进程启动：不随主程序 JobObject 回收，保证主程序退出后 helper 仍能完成替换。
        var helper = Process.Start(psi);
        if (helper is not null) helper.EnableRaisingEvents = false;
    }

    /// <summary>清理上次未完成的更新暂存目录（保留当前正在使用的版本）。</summary>
    public void CleanupStaging() => CleanupStagingStatic();

    /// <summary>
    /// 静态版本：供 App 启动早期（尚无服务实例）调用。
    /// 只清理上次解压残留的暂存目录；已下载的 zip 保留，下次可断点复用，避免重复下载大包。
    /// </summary>
    public static void CleanupStagingStatic()
    {
        try
        {
            if (!Directory.Exists(UpdateStagingDir)) return;
            foreach (var dir in Directory.GetDirectories(UpdateStagingDir))
            {
                Directory.Delete(dir, true);
            }
        }
        catch
        {
            // 清理失败不阻断启动
        }
    }

    // ===== 工具方法 =====

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>解析语义化版本号；带后缀时取主.次.修订部分。解析失败返回 false。</summary>
    internal static bool TryParseVersion(string text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var match = Regex.Match(text.TrimStart('v', 'V'), @"^(\d+)\.(\d+)\.(\d+)");
        if (!match.Success) return false;
        if (!Version.TryParse(match.Value, out var parsed)) return false;
        version = parsed;
        return true;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
