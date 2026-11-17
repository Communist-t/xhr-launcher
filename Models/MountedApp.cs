namespace Launcher.Models;

/// <summary>
/// 被挂载的外部 exe 程序配置项
/// </summary>
public class MountedApp
{
    /// <summary>唯一标识</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>显示名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>一行描述</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>可执行文件路径（相对程序根目录或绝对路径）。解释器模式下指向 pythonw.exe 等启动器。</summary>
    public string ExePath { get; set; } = string.Empty;

    /// <summary>启动参数（可为空；解释器模式下为要执行的脚本名，如 gui.py）</summary>
    public string? Arguments { get; set; }

    /// <summary>工作目录（相对程序根目录或绝对路径；默认为 exe 所在目录）</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>图标路径（相对程序根目录或绝对路径，可为空，为空则用首字母占位）</summary>
    public string? IconPath { get; set; }

    /// <summary>卡片封面图路径（相对程序根目录或绝对路径，可选；为空则回退图标/首字母占位）</summary>
    public string? CoverPath { get; set; }

    /// <summary>卡片标签，逗号分隔（可选）</summary>
    public string? Tags { get; set; }

    /// <summary>子程序 API 配置文件名（相对工作目录），为空默认 user_config.json；登录会话的 ApiKey 会同步写入该文件的 lobster.api_key。文件不存在则跳过同步。</summary>
    public string? ConfigFile { get; set; }

    /// <summary>员工分类（预留，用于云端拉取的员工分组）：wechat / copywriting / operations；现有本地员工不参与分组。</summary>
    public string Category { get; set; } = "";
}
