<#
.SYNOPSIS
  Installs BepInEx 5 (the code-injection layer the ModKit runs on) into Void Tyrant.

.DESCRIPTION
  Downloads the official BepInEx x64 Mono release and extracts it next to "Void Tyrant.exe".
  Uninstall by deleting winhttp.dll, doorstop_config.ini, .doorstop_version and the BepInEx folder
  (or run with -Uninstall). Steam "Verify integrity of game files" will not remove it.
#>
param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Void Tyrant",
    [string]$Version = "5.4.23.5",
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path (Join-Path $GameDir "Void Tyrant.exe"))) {
    throw "Void Tyrant.exe not found in '$GameDir'. Pass -GameDir to point at your install."
}

$loaderFiles = @("winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt", "BepInEx")

if ($Uninstall) {
    foreach ($f in $loaderFiles) {
        $p = Join-Path $GameDir $f
        if (Test-Path $p) { Remove-Item $p -Recurse -Force -Confirm:$false; Write-Host "Removed $p" }
    }
    Write-Host "BepInEx removed. Your Mods folder was left in place."
    return
}

$tmp = Join-Path ([IO.Path]::GetTempPath()) "bepinex-$Version-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force $tmp | Out-Null
try {
    $zip = Join-Path $tmp "bepinex.zip"
    $url = "https://github.com/BepInEx/BepInEx/releases/download/v$Version/BepInEx_win_x64_$Version.zip"
    Write-Host "Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    Expand-Archive -Path $zip -DestinationPath $GameDir -Force
} finally {
    Remove-Item $tmp -Recurse -Force -Confirm:$false
}

# Turn the log console on by default; BepInEx fills in the rest of the config on first launch.
$cfgDir = Join-Path $GameDir "BepInEx\config"
$cfg = Join-Path $cfgDir "BepInEx.cfg"
if (-not (Test-Path $cfg)) {
    New-Item -ItemType Directory -Force $cfgDir | Out-Null
    Set-Content -Path $cfg -Encoding UTF8 -Value @"
[Logging.Console]

## Enables showing a console for log output.
Enabled = true
"@
}

New-Item -ItemType Directory -Force (Join-Path $GameDir "Mods") | Out-Null

Write-Host ""
Write-Host "BepInEx $Version installed to $GameDir"
Write-Host "Launch the game once so BepInEx can finish setting up, then run 'dotnet build' in this repo."
