# 从源码构建 DSH 便携版
#
# 需要：
#   - Windows x64
#   - .NET Framework 4.x（csc.exe）
#   - Node.js 24（同时作为便携运行时来源）
#   - 可选：7-Zip（设置了 -SevenZip 时用 7z 生成 zip，否则用 .NET 压缩）
#
# 示例：
#   powershell -ExecutionPolicy Bypass -File .\build\build.ps1 `
#       -NodeDir "D:\node.js" -DshVersion "0.2.1-rc.2" -OutDir ".\dist"

param(
    [Parameter(Mandatory = $true)][string]$NodeDir,
    [string]$DshVersion = "0.2.1-rc.2",
    [string]$OutDir = ".\dist",
    [string]$WorkDir = ".\build\.work",
    [string]$SevenZip = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$node = Join-Path $NodeDir "node.exe"
$npm = Join-Path $NodeDir "npm.cmd"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $node)) { throw "找不到 $node" }
if (-not (Test-Path $csc)) { throw "找不到 csc.exe（需要 .NET Framework 4.x）" }

$work = [IO.Path]::GetFullPath((Join-Path $repo $WorkDir))
$out = [IO.Path]::GetFullPath((Join-Path $repo $OutDir))
$appDir = Join-Path $work "app"
$runtimeDir = Join-Path $work "runtime"
$payloadDir = Join-Path $work "payload"
$dist = Join-Path $out "DSH-Portable"

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $appDir, $runtimeDir, $payloadDir, $dist | Out-Null

Write-Host "==> 1/5 安装 @deepseek-ai/dsh@$DshVersion"
& $npm install --prefix $appDir --omit=dev --no-audit --no-fund "@deepseek-ai/dsh@$DshVersion"
if ($LASTEXITCODE -ne 0) { throw "npm install 失败" }

Write-Host "==> 2/5 准备 Node.js 运行时"
Copy-Item (Join-Path $NodeDir "*") $runtimeDir -Recurse -Force

function New-Zip([string]$SourceDir, [string]$ZipPath, [bool]$IncludeBase) {
    if ($SevenZip -and (Test-Path $SevenZip)) {
        Push-Location $SourceDir
        try {
            & $SevenZip a -tzip -mx=5 -mmt=on -y $ZipPath "*" | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "7z 失败：$ZipPath" }
        }
        finally { Pop-Location }
    }
    else {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::CreateFromDirectory($SourceDir, $ZipPath,
            [IO.Compression.CompressionLevel]::Optimal, $IncludeBase)
    }
}

Write-Host "==> 3/5 生成 payload"
New-Zip $appDir (Join-Path $payloadDir "app.zip") $false
New-Zip $runtimeDir (Join-Path $payloadDir "runtime.zip") $false

Write-Host "==> 4/5 编译启动器"
& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
    /win32icon:"$repo\src\app.ico" /out:"$dist\DSH-Portable.exe" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    "$repo\src\Launcher.cs"
if ($LASTEXITCODE -ne 0) { throw "csc 编译失败" }

Write-Host "==> 5/5 组装发行目录"
New-Item -ItemType Directory -Force `
    "$dist\payload", "$dist\home", "$dist\workspace", "$dist\logs", "$dist\licenses" | Out-Null
Copy-Item "$payloadDir\app.zip", "$payloadDir\runtime.zip" "$dist\payload" -Force
Copy-Item "$repo\README.md" "$dist\README.md" -Force
Copy-Item "$repo\LICENSE" "$dist\LICENSE" -Force
Copy-Item "$repo\THIRD-PARTY-NOTICES.md" "$dist\THIRD-PARTY-NOTICES.md" -Force
Copy-Item "$repo\licenses\*" "$dist\licenses" -Force

@'
@echo off
setlocal EnableExtensions
chcp 65001 >nul
title DSH 便携版（命令行模式）
cd /d "%~dp0"
set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
set "CACHE=%LOCALAPPDATA%\DSHP"
set "DSH_HOME=%ROOT%\home\.dsh"
set "DSH_AGENTS_HOME=%ROOT%\home\.agents"
set "DSH_TELEMETRY_DISABLED=1"
if not exist "%ROOT%\payload\app.zip" goto :legacy
if not exist "%CACHE%\a\node_modules\@deepseek-ai\dsh\lib\bin.js" (
    echo   首次运行：正在解压 DSH 到本机缓存，大约 1 分钟...
    if not exist "%CACHE%\a" mkdir "%CACHE%\a"
    tar -xf "%ROOT%\payload\app.zip" -C "%CACHE%\a"
)
if not exist "%CACHE%\r\node.exe" (
    echo   正在解压 Node 运行环境...
    if not exist "%CACHE%\r" mkdir "%CACHE%\r"
    tar -xf "%ROOT%\payload\runtime.zip" -C "%CACHE%\r"
)
set "NODE=%CACHE%\r\node.exe"
set "APP=%CACHE%\a"
goto :run
:legacy
set "NODE=%ROOT%\runtime\node.exe"
set "APP=%ROOT%\app"
:run
for %%I in ("%NODE%") do set "NODE_DIR=%%~dpI"
set "PATH=%NODE_DIR%;%PATH%"
if not exist "%ROOT%\home\.dsh" mkdir "%ROOT%\home\.dsh"
if not exist "%ROOT%\workspace" mkdir "%ROOT%\workspace"
cd /d "%ROOT%\workspace"
"%NODE%" "%APP%\node_modules\@deepseek-ai\dsh\lib\bin.js" web
pause >nul
'@ | Set-Content "$dist\启动DSH-命令行版.cmd" -Encoding UTF8

Write-Host ""
Write-Host "完成：$dist"
Get-ChildItem $dist | Format-Table Mode, Length, Name -AutoSize
