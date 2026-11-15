# Sends diagnostics commands to the running game (needs [Debug] Diagnostics = true) and waits until they are done.
# Commands: see Execute() in src\Plugin\Diagnostics.cs. Screenshots land in <game>\BepInEx\cozyvr-out.
#
#   .\build\diag.ps1 'grab on', 'wait 1', 'eyeshot test', 'dump'
#   .\build\diag.ps1 dump -Grep 'marbles:|panels:'          show only matching lines of the new log output
#   powershell -File build/diag.ps1 'grab on; wait 1; dump'  from another shell: one string, separated by ; or ,
param(
    [Parameter(Mandatory, Position = 0)][string[]]$Commands,
    [string]$Grep,
    [int]$TimeoutSeconds = 120
)
. (Join-Path $PSScriptRoot 'common.ps1')

# from another shell (powershell -File ... 'a; b; c') the list arrives as one string
$Commands = @($Commands | ForEach-Object { $_ -split '[;,]' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

$bep = Join-Path $GameRoot 'BepInEx'
$log = Join-Path $bep 'LogOutput.log'
$cmdFile = Join-Path $bep 'cozyvr-cmd.txt'
if (-not (Get-Process 'Cozy Marbles' -ErrorAction SilentlyContinue)) { throw 'The game is not running.' }
$cfg = Join-Path $bep 'config\tobse.cozymarbles.vr.cfg'
if (-not (Select-String -Path $cfg -Pattern '^\s*Diagnostics\s*=\s*true' -Quiet)) {
    throw "[Debug] Diagnostics is off in $cfg - the running game ignores commands. Set it to true and restart the game."
}

$marker = 'diag-done-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$start = if (Test-Path $log) { (Get-Content $log).Count } else { 0 }
# write to a temp file and move it, so the game never reads a half-written file
Set-Content -Path "$cmdFile.tmp" -Value ($Commands + "log $marker") -Encoding ASCII
Move-Item "$cmdFile.tmp" $cmdFile -Force

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (-not (Select-String -Path $log -Pattern $marker -SimpleMatch -Quiet)) {
    if ((Get-Date) -gt $deadline) {
        Remove-Item $cmdFile -ErrorAction SilentlyContinue   # do not leave commands behind for a later start
        throw "Timed out after $TimeoutSeconds s (game still loading, or the commands take longer than -TimeoutSeconds?)"
    }
    Start-Sleep -Milliseconds 300
}
$new = Get-Content $log | Select-Object -Skip $start
if ($Grep) { $new = $new | Select-String -Pattern $Grep | ForEach-Object { $_.Line } }
$new | ForEach-Object { if ($_.Length -gt 600) { $_.Substring(0, 600) + ' ...' } else { $_ } }
