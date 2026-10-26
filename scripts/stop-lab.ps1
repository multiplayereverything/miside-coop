# Stops only the lab processes recorded by launch-lab.ps1. Never touches a user-started game.
$mod = Split-Path -Parent $PSScriptRoot
$pids = Join-Path $mod 'private\lab-pids.txt'
if (-not (Test-Path $pids)) { Write-Host 'No lab PIDs recorded.'; return }
foreach ($line in Get-Content $pids) {
    $id = 0
    if (-not [int]::TryParse($line.Trim(), [ref]$id)) { continue }
    $p = Get-Process -Id $id -ErrorAction SilentlyContinue
    if ($p -and $p.ProcessName -eq 'MiSideFull') { Stop-Process -Id $id -Force; Write-Host "Stopped $id" }
}
Remove-Item $pids
