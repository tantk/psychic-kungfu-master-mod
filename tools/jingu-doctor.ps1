# jingu-doctor.ps1 — interactive mod health-check + repair.
#
# Reports the state of every file the mod depends on:
#   1. Game install (JinGu.exe)
#   2. MelonLoader bootstrap (version.dll + MelonLoader\ folder)
#   3. Patched corlibs (4 .dll files Steam loves to revert)
#   4. The mod itself (JinGuCheats.dll + jingu-cheats-ui.exe)
#
# If anything is reverted/missing, offers a one-key repair. Designed to live in
# the game's Mods\ folder so $PSScriptRoot resolves the game path relatively.

$ErrorActionPreference = "Continue"

# Locate the game folder. When the doctor ships inside Mods\, parent of $PSScriptRoot
# is the game install. Fall back to the standard Steam path otherwise.
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ($scriptDir -and (Test-Path "$scriptDir\..\JinGu.exe")) {
    $gameDir = Resolve-Path "$scriptDir\.." | Select-Object -ExpandProperty Path
} elseif (Test-Path "C:\Program Files (x86)\Steam\steamapps\common\JinGu\JinGu\JinGu.exe") {
    $gameDir = "C:\Program Files (x86)\Steam\steamapps\common\JinGu\JinGu"
} else {
    Write-Host ""
    Write-Host "  Could not locate JinGu install folder." -ForegroundColor Red
    Write-Host "  Expected one of:" -ForegroundColor DarkGray
    Write-Host "    <doctor-folder>\..\JinGu.exe"
    Write-Host "    C:\Program Files (x86)\Steam\steamapps\common\JinGu\JinGu\JinGu.exe"
    Read-Host "  Press Enter to exit"
    exit 1
}

$ml = "$gameDir\MelonLoader\Dependencies\MonoBleedingEdgePatches"
$mg = "$gameDir\JinGu_Data\Managed"

function Status-Line($ok, $label, $detail = "") {
    if ($ok) {
        Write-Host "  ✓ " -ForegroundColor Green -NoNewline
        Write-Host "$label" -NoNewline
    } else {
        Write-Host "  ✗ " -ForegroundColor Red -NoNewline
        Write-Host "$label" -ForegroundColor Red -NoNewline
    }
    if ($detail) { Write-Host "  $detail" -ForegroundColor DarkGray } else { Write-Host "" }
}

Clear-Host
Write-Host ""
Write-Host "  ╔══════════════════════════════════════════════════╗" -ForegroundColor Yellow
Write-Host "  ║     JinGu Cheats · Mod Health Check & Repair    ║" -ForegroundColor Yellow
Write-Host "  ╚══════════════════════════════════════════════════╝" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Game folder: " -NoNewline; Write-Host "$gameDir" -ForegroundColor DarkGray
Write-Host ""

# --- Game ---
$gameOk = Test-Path "$gameDir\JinGu.exe"
Status-Line $gameOk "JinGu.exe (Steam install)" $(if (-not $gameOk) { "not found" })

# --- MelonLoader ---
$mlOk = (Test-Path "$gameDir\version.dll") -and (Test-Path "$gameDir\MelonLoader")
Status-Line $mlOk "MelonLoader bootstrap" $(if (-not $mlOk) { "install MelonLoader first" })

$patchOk = Test-Path $ml
Status-Line $patchOk "MelonLoader patches folder" $(if (-not $patchOk) { "$ml missing — reinstall MelonLoader" })

# --- Stale Unity Doorstop config (xyzsesame's issue #1) ---
# MelonLoader v0.6+ embeds its bootstrap config inside version.dll. If a legacy
# doorstop_config.ini exists in the game folder, Unity Doorstop reads it instead
# of the embedded config and can hang the game at the splash screen. The file is
# NOT shipped by us — it's a leftover from old MelonLoader (≤ 0.5.x), BepInEx,
# or another Doorstop-based mod the user installed previously. We refuse to
# delete it automatically because it might belong to a different mod the user
# still wants — but we flag it loudly.
$doorstopIni = "$gameDir\doorstop_config.ini"
if (Test-Path $doorstopIni) {
    Status-Line $false "Stale doorstop_config.ini detected" "this likely blocks MelonLoader on launch"
    Write-Host ""
    Write-Host "    Found:  $doorstopIni" -ForegroundColor Yellow
    Write-Host "    This file is NOT shipped by JinGu Cheats. It's a leftover from an" -ForegroundColor DarkGray
    Write-Host "    older MelonLoader (≤ v0.5.x), BepInEx, or another Doorstop-based mod." -ForegroundColor DarkGray
    Write-Host "    On launch, Unity Doorstop reads it instead of MelonLoader's embedded" -ForegroundColor DarkGray
    Write-Host "    config, which can hang the game at the splash screen (issue #1)." -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "    If you don't have another active Doorstop-based mod on this game," -ForegroundColor Yellow
    Write-Host "    delete the file manually:" -ForegroundColor Yellow
    Write-Host "      del `"$doorstopIni`"" -ForegroundColor Cyan
    Write-Host ""
} else {
    Status-Line $true "No stale doorstop_config.ini"
}

Write-Host ""
Write-Host "  Patched Mono runtime (corlibs that Steam reverts every few weeks):" -ForegroundColor DarkGray

$problems = New-Object System.Collections.ArrayList
if ($patchOk) {
    foreach ($f in "mscorlib.dll","System.dll","System.Core.dll","System.Runtime.dll") {
        $cur = "$mg\$f"; $ref = "$ml\$f"
        if (-not (Test-Path $ref)) {
            Status-Line $true "$f" "patch missing in MelonLoader (skipped)"
            continue
        }
        if (-not (Test-Path $cur)) {
            [void]$problems.Add($f)
            Status-Line $false "$f" "missing from Managed folder"
            continue
        }
        $cs = (Get-Item $cur).Length
        $rs = (Get-Item $ref).Length
        if ($cs -eq $rs) {
            $mb = [math]::Round($cs / 1MB, 2)
            Status-Line $true "$f" "patched ($mb MB)"
        } else {
            [void]$problems.Add($f)
            Status-Line $false "$f" "REVERTED by Steam (managed=$cs, patched=$rs)"
        }
    }
}

# --- Mod files ---
Write-Host ""
Write-Host "  Mod files:" -ForegroundColor DarkGray
foreach ($f in "JinGuCheats.dll","jingu-cheats-ui.exe","pre-launch.bat","pre-launch.ps1") {
    $p = "$gameDir\Mods\$f"
    $ok = Test-Path $p
    Status-Line $ok "$f" $(if (-not $ok) { "missing — reinstall the mod" })
}

# --- Steam Launch Option hint ---
Write-Host ""
Write-Host "  Auto-heal on every Steam launch:" -ForegroundColor DarkGray
$logPath = "$env:LOCALAPPDATA\JinGuCheats\pre-launch.log"
if (Test-Path $logPath) {
    $lastRun = (Get-Item $logPath).LastWriteTime
    $hours = [math]::Round((New-TimeSpan -Start $lastRun -End (Get-Date)).TotalHours, 1)
    Status-Line $true "pre-launch.bat last ran" "$lastRun  ($hours hours ago)"
} else {
    Status-Line $false "pre-launch.bat has NEVER run" "you probably don't have the Steam Launch Option set"
    Write-Host ""
    Write-Host "    To prevent this issue forever, paste this into Steam:" -ForegroundColor Yellow
    Write-Host "      Right-click JinGu → Properties → General → Launch Options" -ForegroundColor DarkGray
    Write-Host '      "' -NoNewline; Write-Host "$gameDir\Mods\pre-launch.bat" -ForegroundColor Cyan -NoNewline; Write-Host '" %command%'
}

# --- Repair ---
Write-Host ""
if ($problems.Count -eq 0) {
    Write-Host "  ✓ All checks passed. The mod should load correctly on next launch." -ForegroundColor Green
    Write-Host ""
    Read-Host "  Press Enter to exit"
    exit 0
}

Write-Host "  ⚠ Found $($problems.Count) file(s) that need repair." -ForegroundColor Yellow
Write-Host ""
$ans = Read-Host "  Restore them now? (Y/N)"
if ($ans -notmatch '^[Yy]') {
    Write-Host "  Cancelled. The mod will fail to load until you fix the corlibs." -ForegroundColor DarkGray
    Read-Host "  Press Enter to exit"
    exit 0
}

Write-Host ""
foreach ($f in $problems) {
    $cur = "$mg\$f"; $ref = "$ml\$f"
    try {
        if (Test-Path $cur) { attrib -R $cur 2>$null }
        Copy-Item $ref $cur -Force -ErrorAction Stop
        Write-Host "  ✓ restored $f" -ForegroundColor Green
    } catch {
        Write-Host "  ✗ failed to restore $f : $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "    Try right-clicking JinGu-Doctor.bat and choosing 'Run as administrator'." -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "  Done. Launch the game through Steam to verify." -ForegroundColor Green
Read-Host "  Press Enter to exit"
