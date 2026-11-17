<#
.SYNOPSIS
  x.HR Launcher 一键发布：dotnet publish → 放入物料 → 清洗运行态 → 打绿色免安装 zip → 生成校验清单。

.DESCRIPTION
  两种物料来源：
    1) -MaterialDir <目录>：CI/本地已解压好的物料目录（内含 apps\、appr\），直接复制进发布输出；
    2) 不传 -MaterialDir：回退使用源码树内的 apps\、appr\（仅本机联调用，公开仓里这两目录已被 .gitignore 排除）。

  产物输出到 dist\：
    x.HR_<版本>_win-x64.zip
    x.HR_<版本>_win-x64.zip.sha256
    release-manifest.json（版本、物料版本、文件清单与校验值）

.PARAMETER MaterialDir
  解压后的物料根目录（其下应有 apps\ 与 appr\）。

.PARAMETER Version
  覆盖发布版本号；默认取 build\version.json 的 launcher_version。

.PARAMETER Configuration
  Release / Debug，默认 Release。

.EXAMPLE
  pwsh -File scripts\publish.ps1
  pwsh -File scripts\publish.ps1 -MaterialDir .\build\material-stage
#>
param(
  [string]$MaterialDir = "",
  [string]$Version = "",
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
$root = Split-Path -Parent $scriptDir
$buildDir = Join-Path $root "build"
$distDir  = Join-Path $root "dist"

$versionPath = Join-Path $buildDir "version.json"
if (-not (Test-Path $versionPath)) { throw "缺少 $versionPath" }
$versionInfo = Get-Content $versionPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $Version) { $Version = $versionInfo.launcher_version }
$materialVersion  = $versionInfo.runtime_material_version
$rid               = $versionInfo.runtime_rid

# ---- 1. 准备干净的发布输出目录 ----
$publishOut = Join-Path $buildDir ("publish-" + $Version)
if (Test-Path $publishOut) { Remove-Item -LiteralPath $publishOut -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishOut | Out-Null

# ---- 2. dotnet publish ----
Write-Host "==> dotnet publish ($Configuration, $rid)"
$publishArgs = @(
  "publish",
  (Join-Path $root "Launcher.csproj"),
  "-c", $Configuration,
  "-r", $rid,
  "--self-contained", "true",
  "-o", $publishOut,
  "-p:PublishSingleFile=true",
  "-p:IncludeNativeLibrariesForSelfExtract=true",
  "-p:EnableCompressionInSingleFile=true",
  "-v", "minimal"
)
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败，退出码 $LASTEXITCODE" }

# ---- 3. 放入物料 ----
Write-Host "==> 放置运行时物料（物料版本 $materialVersion）"
$materialSource = if ($MaterialDir) {
  [System.IO.Path]::GetFullPath((Join-Path $root $MaterialDir))
} else { $root }
foreach ($slot in @("apps", "appr")) {
  $srcSlot = Join-Path $materialSource $slot
  $dstSlot = Join-Path $publishOut $slot
  if (Test-Path $srcSlot) {
    New-Item -ItemType Directory -Force -Path $dstSlot | Out-Null
    Copy-Item -Path (Join-Path $srcSlot "*") -Destination $dstSlot -Recurse -Force
  } else {
    Write-Warning "物料槽位不存在：$srcSlot"
  }
}

# ---- 4. 清洗发布包里的运行态 / 个人状态 / 探针脚本 ----
$cleanGlobs = @(
  "**/logs/**", "**/screenshots/**", "**/conversation_memory/**",
  "**/events/**", "**/__pycache__/**", "**/_crops/**", "**/_probe_tmp/**",
  "**/*.pid", "**/_probe_*.py", "**/_test_*.py", "**/_regress_*.py",
  "**/probe_uia.py", "**/calibrate_*.py", "**/*.bak", "**/*.bak2", "**/*.bak3",
  "**/my_contacts.json", "**/chat_strategy_config.json", "**/moments_queue.json",
  "**/downloaded_skills.json", "**/user_config.local.json"
)
foreach ($glob in $cleanGlobs) {
  Get-ChildItem -LiteralPath $publishOut -Recurse -Force -File -Filter ($glob -replace '^\*\*/', '') -ErrorAction SilentlyContinue |
    Where-Object { $rel = $_.FullName.Substring($publishOut.Length); ($glob -replace '^\*\*/', '') -notmatch '[\*\[\]]' -or $_.Name -like ($glob -replace '^\*\*/', '') } |
    Remove-Item -Force -ErrorAction SilentlyContinue
}

# 兜底：按路径关键词精确删除已知运行态文件
$stateFiles = @(
  "apps\WeChatRPA\runtime\my_contacts.json",
  "apps\WeChatRPA\runtime\chat_strategy_config.json",
  "apps\WeChatRPA\runtime\moments_queue.json",
  "apps\WeChatRPA\runtime\downloaded_skills.json"
)
foreach ($sf in $stateFiles) {
  $fp = Join-Path $publishOut $sf
  if (Test-Path $fp) { Remove-Item -LiteralPath $fp -Force }
}

# ---- 5. 生成发布清单（文件 + SHA256）----
$fileEntries = @()
Get-ChildItem -LiteralPath $publishOut -Recurse -File -Force | ForEach-Object {
  $rel = $_.FullName.Substring($publishOut.Length).TrimStart('\', '/') -replace '\\', '/'
  $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLower()
  $fileEntries += [pscustomobject]@{ path = $rel; size = $_.Length; sha256 = $hash }
}

$manifest = [pscustomobject]@{
  launcher_version      = $Version
  runtime_material_version = $materialVersion
  rid                    = $rid
  published_at           = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")
  file_count             = $fileEntries.Count
  files                  = $fileEntries
}

# ---- 6. 打 zip ----
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$zipName = "x.HR_" + $Version + "_" + $rid + ".zip"
$zipOut = Join-Path $distDir $zipName
if (Test-Path $zipOut) { Remove-Item -LiteralPath $zipOut -Force }
Compress-Archive -Path (Join-Path $publishOut "*") -DestinationPath $zipOut -CompressionLevel Optimal

$zipHash = (Get-FileHash -LiteralPath $zipOut -Algorithm SHA256).Hash.ToLower()
$zipSizeMB = [math]::Round((Get-Item -LiteralPath $zipOut).Length / 1MB, 1)

# 校验清单：zip 自身哈希 + 包内文件清单
$shaOut = "$zipOut.sha256"
"$zipHash  $zipName" | Out-File -LiteralPath $shaOut -Encoding ascii

$manifestOut = Join-Path $distDir ("release-manifest_" + $Version + ".json")
$manifest | ConvertTo-Json -Depth 8 | Out-File -LiteralPath $manifestOut -Encoding utf8

Write-Host ""
Write-Host "=== 发布完成 ==="
Write-Host "Launcher 版本 : $Version"
Write-Host "物料版本       : $materialVersion"
Write-Host "包内文件数     : $($fileEntries.Count)"
Write-Host "zip 路径       : $zipOut"
Write-Host "zip 大小       : $zipSizeMB MB"
Write-Host "zip SHA256     : $zipHash"
Write-Host "清单           : $manifestOut"
