using System.IO;
using System.Text.Json;
using Launcher.Models;

namespace Launcher.Services;

/// <summary>
/// config.json 根结构
/// </summary>
public class LauncherConfig
{
    /// <summary>后端 API 地址，如 http://127.0.0.1:8080</summary>
    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:8080";

    public List<MountedApp> Apps { get; set; } = new();
}

/// <summary>
/// 负责 config.json 的读写。配置文件放在 exe 同目录下。
/// </summary>
public class ConfigService
{
    private readonly string _configPath;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public LauncherConfig Config { get; private set; } = new();

    public ConfigService()
    {
        var baseDir = AppContext.BaseDirectory;
        _configPath = Path.Combine(baseDir, "config.json");
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_configPath))
        {
            Config = DefaultConfig();
            Save();
            return;
        }

        var json = File.ReadAllText(_configPath);
        Config = JsonSerializer.Deserialize<LauncherConfig>(json, JsonOpts) ?? DefaultConfig();
    }

    /// <summary>
    /// 默认配置：挂载随程序一同分发的内部员工——
    /// 1) 微信智能客服 RPA（便携 Python + 业务脚本）；2) matrixmedia 矩阵工具（Electron）；
    /// 3) 首席办公助理（Electron 全场景办公 Agent，当前为下载型占位框架，产物就位后再填启动路径）。
    /// 所有路径均相对程序根目录（exe 所在目录），整目录拷贝/迁移后无需改配置。
    /// </summary>
    private static LauncherConfig DefaultConfig() => new()
    {
        ApiBaseUrl = "http://saas-ai.cn:8080",
        Apps = new List<MountedApp>
        {
            new()
            {
                Id = "wechat-rpa",
                Name = "微信客服专员",
                Description = "值守微信聊天、及时回复客户，并按队列发布朋友圈",
                ExePath = @"apps\WeChatRPA\runtime\python\pythonw.exe",
                Arguments = "gui.py",
                WorkingDirectory = @"apps\WeChatRPA\runtime",
                IconPath = @"apps\WeChatRPA\runtime\card-icon.png",
                CoverPath = @"apps\WeChatRPA\runtime\card-icon.png",
                Tags = "智能销售,售后服务,链接推销,数据统计"
            },
            new()
            {
                Id = "matrix-video",
                Name = "矩阵运营专员",
                Description = "负责多账号内容的分发运营与账号维护",
                ExePath = @"appr\matrixmedia\matrixmedia.exe",
                Arguments = "",
                WorkingDirectory = @"appr\matrixmedia",
                IconPath = @"appr\matrixmedia\matrixmedia.ico",
                CoverPath = @"appr\matrixmedia\cover.png",
                Tags = "内容创作,多平台分发,数据分析,账号管理"
            },
            new()
            {
                Id = "general-assistant",
                Name = "首席办公助理",
                Description = "统筹文档、表格、演示、浏览器与定时任务等各类办公事务，可调度多个专用智能体协同完成长任务",
                ExePath = "",
                Arguments = "",
                WorkingDirectory = "",
                IconPath = "",
                CoverPath = "",
                Tags = "文档处理,浏览器自动化,邮件协作,定时任务",
                ConfigFile = null
            }
        }
    };

    public void Save()
    {
        var json = JsonSerializer.Serialize(Config, JsonOpts);
        File.WriteAllText(_configPath, json);
    }
}
