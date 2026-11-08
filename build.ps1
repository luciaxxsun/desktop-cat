# 编译 DesktopCat.exe（单文件，素材内嵌）
# 用法：powershell -ExecutionPolicy Bypass -File build.ps1
# 可选参数：-Assets <素材目录> -Out <输出文件名>

param(
    [string]$Assets = "assets",
    [string]$Out = "DesktopCat.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) { throw "找不到 csc.exe：需要 .NET Framework 4.x" }

$assetDir = Join-Path $root $Assets
if (-not (Test-Path $assetDir)) { throw "找不到素材目录 $assetDir（仓库不含美术素材，见 README 的素材规格）" }

$pngs = Get-ChildItem -Path $assetDir -Filter *.png | Sort-Object Name
if ($pngs.Count -eq 0) { throw "$assetDir 里没有 png" }

$args = @("-nologo", "-optimize+", "-target:winexe", "-out:$Out",
          "-r:System.Windows.Forms.dll", "-r:System.Drawing.dll")

foreach ($p in $pngs) {
    $args += "/resource:$($p.FullName),$($p.Name)"
}

$src = Get-ChildItem -Path (Join-Path $root "src") -Filter *.cs | Sort-Object Name
foreach ($s in $src) { $args += $s.FullName }

Push-Location $root
try {
    & $csc $args
    if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }
    $exe = Join-Path $root $Out
    $mb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "OK: $Out  ($mb MB, $($pngs.Count) 张素材内嵌)"
}
finally { Pop-Location }
