param(
    [string]$GamePath = "C:\Program Files (x86)\Steam\steamapps\common\SunlessSea",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src\SunlessQoL.cs"
$dist = Join-Path $root "dist"
$out = Join-Path $dist "SunlessQoL.dll"
$csc = "C:\Windows\Microsoft.NET\Framework64\v3.5\csc.exe"
$core = Join-Path $GamePath "BepInEx\core"
$managed = Join-Path $GamePath "Sunless Sea_Data\Managed"
$bepinex = Join-Path $core "BepInEx.dll"
$harmony = Join-Path $core "0Harmony.dll"
$unity = Join-Path $managed "UnityEngine.dll"
$unityUi = Join-Path $managed "UnityEngine.UI.dll"
$sunless = Join-Path $managed "Sunless.Game.dll"
$failbetter = Join-Path $managed "Failbetter.Core.dll"

if (!(Test-Path -LiteralPath $src)) { throw "Missing source file: $src" }
if (!(Test-Path -LiteralPath $csc)) { throw "Missing .NET 3.5 compiler: $csc" }
if (!(Test-Path -LiteralPath $bepinex)) { throw "Missing BepInEx. Install BepInEx 5 into the game folder first: $core" }
if (!(Test-Path -LiteralPath $sunless)) { throw "Missing Sunless Sea managed assemblies: $managed" }

New-Item -ItemType Directory -Force -Path $dist | Out-Null

& $csc /nologo /target:library /out:$out `
    /r:$bepinex `
    /r:$harmony `
    /r:$unity `
    /r:$unityUi `
    /r:$sunless `
    /r:$failbetter `
    $src

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Built $out"
Write-Host "Install by copying it to: $GamePath\BepInEx\plugins\SunlessQoL.dll"
