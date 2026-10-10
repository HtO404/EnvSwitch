# EnvSwitch · 写入自检
# 双击本文件即可跑：默认只在副本上验证（不动真文件）；
# 要连真文件一起验（写入→校验→按原文还原→比对 SHA-256），加参数：
#     powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\自检.ps1 -含真文件往返
param([switch]$含真文件往返)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)   # scripts\ 的上一级 = 仓库根
$exe = Join-Path $root 'src\bin\Release\net48\EnvSwitch.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host "找不到 $exe" -ForegroundColor Red
    Write-Host "先编译：dotnet build src\EnvSwitch.csproj -c Release" -ForegroundColor Yellow
    exit 2
}

$args = @('--自检')
if ($含真文件往返) { $args += '--含真文件往返' }

Write-Host "正在自检（$($args -join ' ')）…" -ForegroundColor Cyan
$p = Start-Process -FilePath $exe -ArgumentList $args -PassThru -Wait -NoNewWindow

$report = Join-Path $root 'src\bin\Release\自检报告.txt'
if (Test-Path -LiteralPath $report) {
    Write-Host ''
    Get-Content -LiteralPath $report -Encoding UTF8 | Write-Host
}
if ($p.ExitCode -eq 0) { Write-Host "`n全部通过（退出码 0）" -ForegroundColor Green }
else { Write-Host "`n有未通过项（退出码 $($p.ExitCode)），请把 自检报告.txt 发给维护者" -ForegroundColor Red }
exit $p.ExitCode
