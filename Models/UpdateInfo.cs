using System.Text.Json.Serialization;

namespace Launcher.Models;

/// <summary>
/// 远端版本清单（latest.json）条目。由发布流水线随绿色包一起挂到更新源；
/// 客户端拉取后与本地版本做语义化比对。
/// </summary>
public class UpdateInfo
{
    /// <summary>最新版本号，语义化格式：主.次.修订（可带 -beta.1 等后缀）。</summary>
    [JsonPropertyName("launcher_version")]
    public string LauncherVersion { get; set; } = "";

    /// <summary>绿色免安装包下载地址（zip 直链，支持 http/https）。</summary>
    [JsonPropertyName("package_url")]
    public string PackageUrl { get; set; } = "";

    /// <summary>包体 SHA256（小写十六进制），留空则跳过哈希校验。</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    /// <summary>包体字节大小，0 表示未提供。</summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>更新说明 / 变更日志。</summary>
    [JsonPropertyName("release_notes")]
    public string ReleaseNotes { get; set; } = "";

    /// <summary>是否为强制更新（保留字段：当前客户端按推荐更新处理，不做强制拦截）。</summary>
    [JsonPropertyName("mandatory")]
    public bool Mandatory { get; set; }

    /// <summary>本次更新对应的子程序物料版本（仅展示/记录，主程序替换时一并保留）。</summary>
    [JsonPropertyName("runtime_material_version")]
    public string RuntimeMaterialVersion { get; set; } = "";
}
