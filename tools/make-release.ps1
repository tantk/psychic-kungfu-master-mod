# Build the release artifacts that ship to end users.
#
# Run from repo root:  powershell -File tools\make-release.ps1 -Version 0.1.0
# Outputs:
#   release\JinGuCheats-Setup-v<Version>.exe   (primary — Inno Setup wizard, 1-click install)
#   release\JinGuCheats-v<Version>.zip          (fallback — portable layout with install.bat)
#
# The Setup.exe is what users should download. The zip is for power users who'd rather
# inspect / install manually, and as a recovery option if the Setup.exe gets flagged
# by overzealous AV (some engines flag any Inno installer that touches Program Files).

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
Copy-Item "tools\JinGu-Doctor.bat" "$out\JinGu-Doctor.bat"
Copy-Item "tools\jingu-doctor.ps1" "$out\jingu-doctor.ps1"
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

# === Inno Setup installer (the primary user-facing artifact) ===
# Looks in the standard install location for iscc.exe. Skips with a warning if missing.
$iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\iscc.exe"
if (-not (Test-Path $iscc)) {
    $iscc = "C:\Program Files (x86)\Inno Setup 6\iscc.exe"
}
if (Test-Path $iscc) {
    Write-Host ""
    Write-Host "Building installer with Inno Setup..."
    & $iscc "/Qp" "/DMyAppVersion=$Version" "installer\setup.iss"
    if ($LASTEXITCODE -eq 0) {
        $setupExe = "release\JinGuCheats-Setup-v$Version.exe"
        if (Test-Path $setupExe) {
            $size = [math]::Round((Get-Item $setupExe).Length / 1MB, 1)
            Write-Host "Built: $setupExe  ($size MB)"
        }
    } else {
        Write-Warning "Inno Setup compile failed with exit code $LASTEXITCODE"
    }
} else {
    Write-Warning "Inno Setup not found — skipping Setup.exe build."
    Write-Warning "Install it via: winget install JRSoftware.InnoSetup"
}
