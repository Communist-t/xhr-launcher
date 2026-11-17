using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Models;
using Launcher.Services;

namespace Launcher.ViewModels;

/// <summary>
/// 单个程序卡片的 ViewModel
/// </summary>
public partial class MountedAppViewModel : ObservableObject
{
    private readonly ProcessManager _pm;
    private readonly UserConfigSyncService _sync;
    private readonly UserSession _session;

    public MountedApp Model { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "待命中";

    public string Name => Model.Name;
    public string Description => Model.Description;

    /// <summary>产物是否已就位（已配置可执行入口）。未就位的卡片仅作框架占位，不参与启动。</summary>
    public bool IsReady => !string.IsNullOrWhiteSpace(Model.ExePath);

    /// <summary>底部主按钮文案：未就位显示“待下载”，已就位显示“开始工作”。</summary>
    public string ActionText => IsReady ? "开始工作" : "待下载";

    /// <summary>解析后的图标绝对路径（相对路径已基于程序根目录解析）。</summary>
    public string IconPath => PathResolver.Resolve(Model.IconPath);

    /// <summary>图标文件是否存在；不存在时界面回退首字母占位。</summary>
    public bool HasIcon => File.Exists(IconPath);

    /// <summary>解析后的封面图绝对路径（相对路径已基于程序根目录解析）。</summary>
    public string CoverPath => PathResolver.Resolve(Model.CoverPath);

    /// <summary>封面位图；文件不存在或解码失败时为 null，界面回退首字母占位。</summary>
    public BitmapImage? CoverImage { get; }

    /// <summary>封面图是否存在。</summary>
    public bool HasCover => CoverImage != null;

    /// <summary>标签列表（逗号分隔解析，去空白）。</summary>
    public IReadOnlyList<string> Tags =>
        (Model.Tags ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();

    public MountedAppViewModel(MountedApp model, ProcessManager pm, UserConfigSyncService sync, UserSession session)
    {
        Model = model;
        _pm = pm;
        _sync = sync;
        _session = session;

        StatusText = IsReady ? "待命中" : "待下载";

        var cover = PathResolver.Resolve(Model.CoverPath);
        if (File.Exists(cover))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(cover, UriKind.Absolute);
                bmp.EndInit();
                CoverImage = bmp;
            }
            catch
            {
                CoverImage = null;
            }
        }
    }

    private bool CanLaunch() => !IsRunning && IsReady;

    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private void Launch()
    {
        // 未就位（下载型占位卡片，产物尚未下载安装）不启动。
        if (!IsReady) return;

        // 拉起子程序前，先把当前登录账号的 ApiKey 同步进它的配置文件，
        // 避免子程序仍沿用旧账号密钥。
        _sync.Sync(Model, _session);

        var ok = _pm.Launch(Model);
        if (!ok)
        {
            StatusText = "启用失败";
        }
    }

    /// <summary>在客户端内关闭正在运行的子程序（连同其子进程）。</summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _pm.Kill(Model.Id);
    }

    public void UpdateState(bool running)
    {
        IsRunning = running;
        StatusText = running ? "工作中" : (IsReady ? "待命中" : "待下载");
    }
}
