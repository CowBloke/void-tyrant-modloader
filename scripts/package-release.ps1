<#
.SYNOPSIS
  Builds the player download: a zip to extract into the Void Tyrant folder.

.DESCRIPTION
  Contains BepInEx, the ModKit and Debug Panel plugins, the disabled example mod and a short readme.
  Writes dist\VoidTyrantModKit-<version>.zip. Building still needs the game installed for references.
#>
param(
    [string]$GameDir,
    # The game version this release was tested with. Shown in the F1 menu title and BepInEx\LogOutput.log.
    [string]$GameVersion = "1.3.4.11",
    [string]$BepInExVersion = "5.4.23.5",
    [string]$RepoUrl
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root "dist"
$stage = Join-Path $dist "stage"

$plugin = Get-Content (Join-Path $root "src\VoidTyrant.ModKit\ModKitPlugin.cs") -Raw
if ($plugin -notmatch 'const string Version = "([^"]+)"') { throw "Could not find ModKitPlugin.Version" }
$version = $Matches[1]

if (-not $RepoUrl) {
    $RepoUrl = (git -C $root remote get-url origin 2>$null) -replace '\.git$', ''
}

$buildArgs = @("build", (Join-Path $root "VoidTyrantMods.sln"), "-c", "Release", "-p:DeployToGame=false")
if ($GameDir) { $buildArgs += "-p:GameDir=$GameDir" }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force -Confirm:$false }
New-Item -ItemType Directory -Force $stage | Out-Null

# BepInEx: downloaded once and cached.
$cache = Join-Path $dist "cache"
$bepinexZip = Join-Path $cache "BepInEx_win_x64_$BepInExVersion.zip"
if (-not (Test-Path $bepinexZip)) {
    New-Item -ItemType Directory -Force $cache | Out-Null
    $url = "https://github.com/BepInEx/BepInEx/releases/download/v$BepInExVersion/BepInEx_win_x64_$BepInExVersion.zip"
    Write-Host "Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $bepinexZip -UseBasicParsing
}
Expand-Archive -Path $bepinexZip -DestinationPath $stage -Force

# BepInEx's release zip has no license file; its MIT license must ship with the binaries.
$bepinexLicense = Join-Path $cache "BepInEx-$BepInExVersion-LICENSE.txt"
if (-not (Test-Path $bepinexLicense)) {
    Invoke-WebRequest -Uri "https://raw.githubusercontent.com/BepInEx/BepInEx/v$BepInExVersion/LICENSE" `
        -OutFile $bepinexLicense -UseBasicParsing
}
Copy-Item $bepinexLicense (Join-Path $stage "BepInEx\LICENSE.txt")

foreach ($name in @("VoidTyrant.ModKit", "VoidTyrant.DebugPanel")) {
    $dll = Join-Path $root "src\$name\bin\Release\$name.dll"
    $dest = Join-Path $stage "BepInEx\plugins\$name"
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item $dll $dest
}

$mods = Join-Path $stage "Mods"
New-Item -ItemType Directory -Force $mods | Out-Null
Copy-Item (Join-Path $root "examples\ExampleMod") $mods -Recurse

$readme = (Get-Content (Join-Path $PSScriptRoot "release-readme.txt") -Raw).
    Replace("{VERSION}", $version).Replace("{GAME_VERSION}", $GameVersion)
if ($RepoUrl) { $readme = $readme.Replace("{REPO_URL}", $RepoUrl) }
else { $readme = ($readme -split "`r?`n" | Where-Object { $_ -notmatch '\{REPO_URL\}' }) -join "`r`n" }
Set-Content -Path (Join-Path $stage "VoidTyrantModKit-README.txt") -Value $readme -Encoding UTF8
Copy-Item (Join-Path $root "LICENSE") (Join-Path $stage "VoidTyrantModKit-LICENSE.txt")

$zip = Join-Path $dist "VoidTyrantModKit-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force -Confirm:$false }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Remove-Item $stage -Recurse -Force -Confirm:$false

Write-Host ""
Write-Host "Created $zip"
