# x.HR Launcher —— 双仓 CI/CD 搭建说明

## 一、仓库划分

| 仓库 | 内容 | 不进仓库的东西 |
|---|---|---|
| **主仓**（本仓，源码仓） | WPF 源码、`Assets`、工程文件、`scripts/`、`.github/workflows/`、`build/` 版本与清单骨架 | 运行时物料、构建产物、个人状态、密钥 |
| **物料仓**（独立制品仓） | `apps/WeChatRPA`（Python 运行时 + 业务脚本）、`appr/matrixmedia`（Electron 目录）、打包脚本与清单 | 日志、截图、记忆、事件、缓存、探针脚本、个人状态 |

主仓流水线按 `build/version.json` 里的 `runtime_material_version` 去物料仓拉对应版本的物料 zip，再 `dotnet publish` 出绿色免安装包。

## 二、目录与关键文件

```
Launcher/                        主仓
├── .github/workflows/
│   ├── build-release.yml         主仓：拉物料 → publish → 出包 → 发 Release
│   └── build-material.yml        物料仓：打包物料 → 发物料 Release
├── build/
│   ├── version.json              launcher_version / runtime_material_version
│   └── material-manifest.json    物料内容与排除规则
├── scripts/
│   ├── publish.ps1               本机/CI 一键发布
│   └── pack-material.ps1         物料打包
├── Services/ Models/ ViewModels/ Views/ Converters/ Assets/ ...
└── Launcher.csproj
```

物料仓内目录（与主仓发布槽位保持一致）：

```
xhr-materials/
├── apps/WeChatRPA/...
├── appr/matrixmedia/...
├── build/material-manifest.json
└── scripts/pack-material.ps1
```

## 三、本地先跑通（已验证）

```powershell
# 1. 打物料包（产物在 build/material-<version>.zip + .manifest.json）
pwsh -File scripts\pack-material.ps1

# 2. 解压物料到暂存目录，再出主程序包
Expand-Archive build\material-rpa-matrix-1.0.0.zip -DestinationPath build\material-stage -Force
pwsh -File scripts\publish.ps1 -MaterialDir build\material-stage
# 产物：dist\x.HR_<版本>_win-x64.zip + .sha256 + release-manifest_<版本>.json
```

本机无 .NET 9 运行时时，可设 `$env:DOTNET_ROLL_FORWARD='LatestMajor'` 用 SDK 10 构建；CI 用 `actions/setup-dotnet` 装 9.0.x。

## 四、两个 GitHub 仓库的创建与推送

### 1. 物料仓 `xhr-materials`

```powershell
# 在物料物料目录准备物料仓内容（示例：从主仓复制运行时物料与打包脚本）
git init
git add apps appr build/material-manifest.json scripts/pack-material.ps1
git commit -m "chore: 初始化物料仓 rpa-matrix-1.0.0"
git branch -M main
git remote add origin https://github.com/Communist-t/xhr-materials.git
git push -u origin main

# 打物料版本 tag 并推送，触发物料流水线打包发 Release
git tag rpa-matrix-1.0.0
git push origin rpa-matrix-1.0.0
```

> 物料仓若设为私有，主仓需要一个有读权限的 PAT（见下）。

### 2. 主仓

```powershell
cd D:\copyofthefile\x.hr\Launcher
# 确认 .gitignore 已排除 apps/appr/bin/obj/dist/build 暂存产物后再提交
git init
git add .gitignore .github build scripts Services Models ViewModels Views Converters Assets `
      App.xaml App.xaml.cs AssemblyInfo.cs Launcher.csproj MainWindow.xaml MainWindow.xaml.cs app.ico
git commit -m "chore: 初始化主仓"
git branch -M main
git remote add origin https://github.com/Communist-t/xhr-launcher.git
git push -u origin main

# 发正式版：打 v 标签触发主仓流水线出 Release
git tag v0.1.0
git push origin v0.1.0
```

## 五、仓库变量与密钥配置（主仓 Settings）

| 类型 | 名称 | 说明 |
|---|---|---|
| Variables | `MATERIAL_REPO` | 物料仓地址，格式 `Communist-t/xhr-materials`（默认已填，可不改） |
| Secrets | `MATERIAL_TOKEN` | 物料仓私有时填只读 PAT；物料仓公开可留空（流水线回退用 `GITHUB_TOKEN`） |

流水线默认物料仓占位为 `Communist-t/xhr-materials`，与本说明一致；如仓名调整，在主仓 Variables 里覆盖。

## 六、版本对应关系

- 主仓 tag：`v0.1.0` → `launcher_version=0.1.0`
- 物料 tag：`rpa-matrix-1.0.0` → `runtime_material_version=rpa-matrix-1.0.0`
- 主仓 `build/version.json` 的物料版本要与物料仓最新 tag 一致；子程序升级时单独给物料仓打新 tag，再回主仓更新该版本号。

## 七、上线前必须处理的安全事项

1. **吊销已泄露的 Key**：本地历史里出现过的 `7361c7b2-...` 要到后端作废重发；新 Key 不进仓库，登录后由启动器回写 `user_config.json`。
2. **公开仓会暴露**后端域名、接口路径、WPF 源码与聊天 SOP 结构；`chat_strategy_config.json`、`my_contacts.json` 已在排除清单中，不得再提交。
3. 国内最终用户下载 200 MB+ 包，GitHub Release 直链较慢；正式分发建议把 `dist/*.zip` 再同步到对象存储/自有下载页，Release 仅作存档。
