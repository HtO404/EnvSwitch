# 打包给同事：生成「解压即用」的 zip
#
# 用法（任意目录）：
#     powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\打包.ps1
#     powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\打包.ps1 -版本 1.1.1
#
# 产物：<仓库根>\交付包\EnvSwitch-v<版本>-<日期>.zip
#      （zip 里就一个文件夹：EnvSwitch.exe + exe.config + 配置.json + README.md + 启动EnvSwitch.cmd）
param([string]$版本 = "1.1.0")

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)   # scripts\ 的上一级 = 仓库根
$src = Join-Path $root 'src'
$proj = Join-Path $src 'EnvSwitch.csproj'

Write-Host "① 编译 Release(net48)…" -ForegroundColor Cyan
& dotnet build $proj -c Release --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { Write-Host "编译失败" -ForegroundColor Red; exit 1 }

$built = Join-Path $src 'bin\Release\net48'
$stage = Join-Path $root '交付包\EnvSwitch'          # 解压后就是这一层文件夹，名字直观
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

Write-Host "② 组装交付包…" -ForegroundColor Cyan
Copy-Item (Join-Path $built 'EnvSwitch.exe') $stage
if (Test-Path (Join-Path $built 'EnvSwitch.exe.config')) { Copy-Item (Join-Path $built 'EnvSwitch.exe.config') $stage }
# 说明文档（README 等）有就一起打包
foreach ($doc in @('README.md', '第一次使用.md')) {
    $p = Join-Path $root $doc
    if (Test-Path $p) { Copy-Item $p $stage }
}

# 预置配置：让程序自己写一份默认的（目录留空 = 首次运行自动识别），保证与程序内结构一致
Start-Process -FilePath (Join-Path $stage 'EnvSwitch.exe') -ArgumentList @('--写默认配置', $stage) -Wait -NoNewWindow | Out-Null

# 备用入口（cmd 用 GB18030 落盘，cmd.exe 才不会乱码）
$cmdPath = Join-Path $stage '启动EnvSwitch.cmd'
$cmdText = "@echo off`r`nrem EnvSwitch —— 备用入口（正常双击 EnvSwitch.exe 即可）`r`nstart `"`" `"%~dp0EnvSwitch.exe`"`r`n"
[IO.File]::WriteAllText($cmdPath, $cmdText, [Text.Encoding]::GetEncoding(936))

Write-Host "③ 打 zip…" -ForegroundColor Cyan
$zip = Join-Path $root ('交付包\EnvSwitch-v' + $版本 + '-' + (Get-Date -Format 'yyyyMMdd') + '.zip')
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal

Write-Host ""
Write-Host "完成：" -ForegroundColor Green
Get-ChildItem $stage | Select-Object Name, @{n='KB';e={[math]::Round($_.Length/1KB,1)}} | Format-Table -AutoSize | Out-Host
Write-Host ("zip：{0}（{1:N0} KB）" -f $zip, ((Get-Item $zip).Length/1KB))
Write-Host "发这个 zip 给同事即可；对方解压后双击 EnvSwitch.exe。"
