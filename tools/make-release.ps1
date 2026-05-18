# Build the release zip that ships to end users.
#
# Run from repo root:  pwsh tools/make-release.ps1 -Version 0.1.0
# Output:              release/JinGuCheats-v<Version>.zip
#
# The zip lays everything FLAT so install.bat can reference its siblings via %~dp0:
#   JinGuCheats-v0.1.0/
#   ├── install.bat
#   ├── uninstall.bat
#   ├── JinGuCheats.dll
#   ├── jingu-cheats-ui.exe
#   ├── pre-launch.bat
#   ├── pre-launch.ps1
#   ├── README.md
#   └── LICENSE

param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repo

$plugin   = "plugin\bin\Release\JinGuCheats.dll"
$ui       = "ui\src-tauri\target\release\jingu-cheats-ui.exe"
$preBat   = "tools\pre-launch.bat"
$prePs1   = "tools\pre-launch.ps1"
$installBat   = "installer\install.bat"
$uninstallBat = "installer\uninstall.bat"
# MelonLoader 0.7.3 x64 bundled with the release so users don't need a separate download.
# Distributed unchanged (kept as the original zip) — Apache 2.0 license requires preserving
# their LICENSE/NOTICE, which we do by extracting LICENSE.md alongside it.
$melonZip = "downloads\MelonLoader.x64.zip"

foreach ($f in $plugin, $ui, $preBat, $prePs1, $installBat, $uninstallBat, $melonZip, "README.md", "LICENSE") {
    if (-not (Test-Path $f)) { throw "Missing build artifact: $f. Build plugin + UI first." }
}

$out = "release\JinGuCheats-v$Version"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null

Copy-Item $plugin       "$out\JinGuCheats.dll"
Copy-Item $ui           "$out\jingu-cheats-ui.exe"
Copy-Item $preBat       "$out\pre-launch.bat"
Copy-Item $prePs1       "$out\pre-launch.ps1"
Copy-Item $installBat   "$out\install.bat"
Copy-Item $uninstallBat "$out\uninstall.bat"
Copy-Item $melonZip     "$out\MelonLoader.x64.zip"
Copy-Item "README.md"   "$out\README.md"
Copy-Item "LICENSE"     "$out\LICENSE"

# Extract MelonLoader's LICENSE.md to satisfy Apache 2.0 attribution.
$tmpExtract = "release\_ml_tmp"
if (Test-Path $tmpExtract) { Remove-Item $tmpExtract -Recurse -Force }
Expand-Archive -Path $melonZip -DestinationPath $tmpExtract -Force
$mlLicense = "$tmpExtract\MelonLoader\Documentation\LICENSE.md"
if (Test-Path $mlLicense) {
    Copy-Item $mlLicense "$out\MelonLoader-LICENSE.md"
} else {
    Write-Warning "MelonLoader LICENSE.md not found at expected path inside zip — Apache 2.0 attribution missing"
}
Remove-Item $tmpExtract -Recurse -Force

$zip = "release\JinGuCheats-v$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$out\*" -DestinationPath $zip

Write-Host "Built: $zip"
Write-Host "Contents:"
Get-ChildItem $out | ForEach-Object { Write-Host "  $($_.Name)  ($($_.Length) bytes)" }
