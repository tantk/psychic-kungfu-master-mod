# pre-launch.ps1 — silently verify and restore corlibs before the game starts.
# Wired into Steam via Launch Options:
#   "C:\dev\physickungfu_cheat\tools\pre-launch.bat" %command%
#
# The .bat invokes this PowerShell script with -WindowStyle Hidden so the user
# never sees a console flash. Total runtime: ~250 ms.

$ErrorActionPreference = "Stop"
$gameDir = "C:\Program Files (x86)\Steam\steamapps\common\JinGu\JinGu"
$ml      = "$gameDir\MelonLoader\Dependencies\MonoBleedingEdgePatches"
$mg      = "$gameDir\JinGu_Data\Managed"
$logDir  = "$env:LOCALAPPDATA\JinGuCheats"
$log     = "$logDir\pre-launch.log"

# Logs go to %LOCALAPPDATA% — Program Files would require elevation
[void](New-Item -ItemType Directory -Path $logDir -Force -ErrorAction SilentlyContinue)

function W($msg) {
  "$([DateTime]::Now.ToString('yyyy-MM-dd HH:mm:ss')) $msg" | Out-File -FilePath $log -Append -Encoding UTF8
}

W "--- pre-launch start ---"
if (-not (Test-Path $ml)) {
  W "  WARN: patches folder missing ($ml) — skipping corlib check. Game may fail to bootstrap MelonLoader."
  exit 0
}

$restored = 0
foreach ($f in "mscorlib.dll","System.dll","System.Core.dll","System.Runtime.dll") {
  $cur = "$mg\$f"; $ref = "$ml\$f"
  if (-not (Test-Path $ref)) { continue }                 # patches folder may not have all 4 files
  if (-not (Test-Path $cur)) {                            # game ships without the file
    W "  $f missing in Managed — copying from patches"
    Copy-Item $ref $cur -Force; $restored++; continue
  }
  $curLen = (Get-Item $cur).Length
  $refLen = (Get-Item $ref).Length
  if ($curLen -ne $refLen) {
    W "  $f reverted by update ($curLen → $refLen bytes), restoring patch"
    attrib -R $cur 2>$null
    Copy-Item $ref $cur -Force
    $restored++
  }
}

if ($restored -eq 0) {
  W "  all corlibs already patched (no action)"
} else {
  W "  restored $restored corlib(s)"
  # Spawn a background notifier so the user sees that Steam reverted files. We
  # detach (no -Wait) so pre-launch returns immediately and Steam can keep going.
  $notifierScript = @"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
`$n = New-Object System.Windows.Forms.NotifyIcon
`$n.Icon = [System.Drawing.SystemIcons]::Information
`$n.Visible = `$true
`$n.BalloonTipTitle = 'JinGu Cheats'
`$n.BalloonTipText = 'Steam reverted $restored Mono runtime file(s) - restored automatically.'
`$n.BalloonTipIcon = [System.Windows.Forms.ToolTipIcon]::Info
`$n.ShowBalloonTip(7000)
Start-Sleep -Seconds 8
`$n.Dispose()
"@
  try {
    $tmpFile = Join-Path $env:TEMP "jingu-cheats-notify-$([guid]::NewGuid().ToString('N')).ps1"
    $notifierScript | Out-File -FilePath $tmpFile -Encoding UTF8
    Start-Process -FilePath powershell.exe -WindowStyle Hidden -ArgumentList "-NoProfile","-ExecutionPolicy","Bypass","-File",$tmpFile | Out-Null
    W "  notification spawned"
  } catch {
    W "  notification failed (non-fatal): $_"
  }
}
W "--- pre-launch done ---"
exit 0
