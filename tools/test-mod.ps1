<#
.SYNOPSIS
  End-to-end test: launch JinGu, wait for MelonLoader + plugin to bootstrap,
  optionally auto-load most-recent save, query the pipe for state, validate.

.DESCRIPTION
  Used as a smoke test after rebuilding the plugin. Catches regressions in
  ~30s without manual game interaction.

  Pass criteria:
    1. MelonLoader/Latest.log shows "JinGu Cheats ... READY"
    2. No [ERROR] lines after the READY marker
    3. Pipe handshake succeeds (cmd: hello)
    4. State response has in_game=true AND leader_name is non-empty
       (when -AutoLoad is set; otherwise just connection is required)

.PARAMETER AutoLoad
  Enable plugin's [Test] AutoLoadSave option for this run so we get an in-game
  state without manual UI interaction.

.PARAMETER NoCleanup
  Keep the game and UI running after the test. Useful if you want to inspect
  the state by hand after a passing run.

.PARAMETER TimeoutSec
  How long to wait for the plugin to reach READY. Default 30s.

.EXAMPLE
  pwsh tools\test-mod.ps1
  pwsh tools\test-mod.ps1 -AutoLoad
  pwsh tools\test-mod.ps1 -AutoLoad -NoCleanup -TimeoutSec 60
#>
[CmdletBinding()]
param(
  [switch]$AutoLoad,
  [switch]$NoCleanup,
  [int]$TimeoutSec = 30,
  [int]$LoadWaitSec = 25
)

$ErrorActionPreference = "Stop"
$gameDir   = "C:\Program Files (x86)\Steam\steamapps\common\JinGu\JinGu"
$exe       = "$gameDir\JinGu.exe"
$log       = "$gameDir\MelonLoader\Latest.log"
$cfgFile   = "$gameDir\UserData\MelonPreferences.cfg"
$pipeName  = "JinGuCheats.v1"

function Section($msg) { Write-Host ""; Write-Host "=== $msg ===" -ForegroundColor Cyan }
function Pass($msg)    { Write-Host "PASS  $msg" -ForegroundColor Green }
function Fail($msg)    { Write-Host "FAIL  $msg" -ForegroundColor Red }
function Info($msg)    { Write-Host "      $msg" -ForegroundColor Gray }

# ---------- Step 1: Cleanup any prior run ----------
Section "Killing prior game/UI processes"
Get-Process -Name JinGu, jingu-cheats-ui -ErrorAction SilentlyContinue | ForEach-Object {
  Info "killing $($_.Name) (pid $($_.Id))"
  Stop-Process -Id $_.Id -Force
}
Start-Sleep -Milliseconds 800

# ---------- Step 2: Truncate Latest.log so READY detection isn't fooled by old runs ----------
Section "Resetting MelonLoader log"
if (Test-Path $log) {
  try { Clear-Content $log -ErrorAction Stop; Info "cleared $log" }
  catch { Info "could not clear (locked?); will rely on timestamps: $($_.Exception.Message)" }
}

# ---------- Step 3: Launch game via Steam ----------
Section "Launching JinGu via Steam"
$marker = "$gameDir\JINGU_AUTOLOAD"
if ($AutoLoad) {
  # Marker file is more reliable than env vars when Steam is already running
  # (Steam was launched without our env var, so its children don't see it).
  Info "AutoLoad enabled — touching marker file $marker"
  Set-Content -Path $marker -Value "1" -NoNewline
} else {
  if (Test-Path $marker) { Remove-Item $marker -Force -ErrorAction SilentlyContinue }
}

$launchTime = Get-Date
Start-Process "steam://rungameid/3313720" | Out-Null
Info "steam://rungameid/3313720 invoked at $(Get-Date -Format HH:mm:ss.fff)"

# Wait for JinGu.exe to actually start (Steam takes a few seconds)
$proc = $null
$gameStart = Get-Date
while ((Get-Date) -lt $gameStart.AddSeconds(15)) {
  $proc = Get-Process -Name JinGu -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($proc) { break }
  Start-Sleep -Milliseconds 500
}
if ($proc) {
  Info "JinGu.exe started, PID $($proc.Id) at $(Get-Date -Format HH:mm:ss.fff)"
} else {
  Fail "JinGu.exe never appeared after Steam launch"
  exit 1
}

# ---------- Step 4: Wait for plugin READY ----------
Section "Waiting for plugin to bootstrap (max ${TimeoutSec}s)"
$ready = $false
$readyLine = $null
$start = Get-Date
while ((Get-Date) -lt $start.AddSeconds($TimeoutSec)) {
  if (Test-Path $log) {
    $content = Get-Content $log -Raw -ErrorAction SilentlyContinue
    if ($content -and $content -match '\[JinGu_Cheats\][^\r\n]*READY') {
      $ready = $true
      $readyLine = ($content -split "`n" | Where-Object { $_ -match 'READY' } | Select-Object -Last 1).Trim()
      break
    }
  }
  Start-Sleep -Milliseconds 400
}
if ($ready) {
  $elapsed = ((Get-Date) - $start).TotalSeconds
  Pass ("plugin READY at +{0:N1}s" -f $elapsed)
  Info $readyLine
} else {
  Fail "plugin did not reach READY within ${TimeoutSec}s"
  if (Test-Path $log) {
    Info "--- last 20 lines of log ---"
    Get-Content $log -Tail 20 | ForEach-Object { Info $_ }
  } else {
    Info "no log file created — MelonLoader didn't bootstrap"
  }
  if (-not $NoCleanup) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
  }
  exit 1
}

# ---------- Step 5: Pipe handshake ----------
Section "Pipe handshake"
function Invoke-Pipe([string]$jsonRequest, [int]$timeoutMs = 3000) {
  $client = New-Object System.IO.Pipes.NamedPipeClientStream(".", $pipeName, [System.IO.Pipes.PipeDirection]::InOut)
  try {
    $client.Connect($timeoutMs)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($jsonRequest)
    $header = [BitConverter]::GetBytes([uint32]$bytes.Length)
    $client.Write($header, 0, 4)
    $client.Write($bytes, 0, $bytes.Length)
    $client.Flush()
    $rhdr = New-Object byte[] 4
    [void]$client.Read($rhdr, 0, 4)
    $rlen = [BitConverter]::ToUInt32($rhdr, 0)
    $rbody = New-Object byte[] $rlen
    $read = 0
    while ($read -lt $rlen) {
      $n = $client.Read($rbody, $read, $rlen - $read)
      if ($n -le 0) { break }
      $read += $n
    }
    return [System.Text.Encoding]::UTF8.GetString($rbody) | ConvertFrom-Json
  } finally {
    $client.Dispose()
  }
}

try {
  $hello = Invoke-Pipe '{"cmd":"hello"}'
  if ($hello.ok -and $hello.protocol -ge 1) {
    Pass "handshake ok — plugin=$($hello.plugin) version=$($hello.version) protocol=v$($hello.protocol)"
  } else { Fail "handshake response malformed: $($hello | ConvertTo-Json -Compress)"; exit 2 }
} catch {
  Fail "pipe connection failed: $_"
  if (-not $NoCleanup) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
  exit 2
}

# ---------- Step 6: State validation ----------
Section "Polling /api/state"
if ($AutoLoad) {
  Info "AutoLoad enabled — waiting up to ${LoadWaitSec}s for in_game=true"
}
$state = $null
$loaded = $false
$pollStart = Get-Date
$deadline = if ($AutoLoad) { $pollStart.AddSeconds($LoadWaitSec) } else { $pollStart.AddSeconds(2) }
do {
  try { $state = Invoke-Pipe '{"cmd":"state"}' } catch { Info "state poll error: $_" }
  if ($state -and $state.in_game) { $loaded = $true; break }
  Start-Sleep -Milliseconds 600
} while ((Get-Date) -lt $deadline)

if ($state) {
  Info "in_game=$($state.in_game) money=$($state.money) leader=`"$($state.leader_family)$($state.leader_name)`" gameTime=$($state.game_time) errors=$($state.error_count)"
}
if ($AutoLoad) {
  if ($loaded -and $state.leader_name) {
    Pass "save loaded — leader=`"$($state.leader_family)$($state.leader_name)`""
  } else {
    Fail "save did not load within ${LoadWaitSec}s (in_game=$($state.in_game))"
    $exitCode = 3
  }
} else {
  Pass "pipe state poll succeeded"
}

# ---------- Step 7: Check for ERROR lines after READY ----------
Section "Scanning log for errors since READY"
$lines = Get-Content $log
$readyIdx = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
  if ($lines[$i] -match 'JinGu_Cheats.*READY') { $readyIdx = $i }
}
if ($readyIdx -ge 0) {
  $after = $lines[($readyIdx+1)..($lines.Count-1)]
  $errs = $after | Where-Object { $_ -match '\[ERROR\]' }
  if ($errs.Count -gt 0) {
    Fail "$($errs.Count) error line(s) after READY:"
    $errs | Select-Object -First 5 | ForEach-Object { Info $_ }
    $exitCode = 4
  } else {
    Pass "no errors after READY"
  }
}

# ---------- Step 8: Cleanup ----------
# Always remove marker so future manual game launches don't auto-load.
if (Test-Path $marker) { Remove-Item $marker -Force -ErrorAction SilentlyContinue }
if (-not $NoCleanup) {
  Section "Cleanup"
  Get-Process -Name JinGu, jingu-cheats-ui -ErrorAction SilentlyContinue | ForEach-Object {
    Info "killing $($_.Name) (pid $($_.Id))"
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
  }
} else {
  Info ""
  Info "NoCleanup set — game (pid $($proc.Id)) left running for inspection."
}

if ($exitCode) { exit $exitCode } else { Section "ALL CHECKS PASSED"; exit 0 }
