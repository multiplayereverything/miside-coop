# Starts isolated lab copies (private\lab-host, private\lab-client, ...) with their windows hidden.
# Test-owned PIDs go to private\lab-pids.txt; stop only those (never a user-owned game process).
param(
    [int]$Players = 2, [int]$Seconds = 0, [switch]$Visible,
    [string]$HostArgs = '', [string]$ClientArgs = '', [string]$Tour = '', [string]$JoinAddress = '127.0.0.1'
)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path $PSScriptRoot -Parent
$labs = @(
    @{ Name = 'host'; Dir = 'lab-host' },
    @{ Name = 'c1'; Dir = 'lab-client' },
    @{ Name = 'c2'; Dir = 'lab-c2' },
    @{ Name = 'c3'; Dir = 'lab-c3' }
) | Select-Object -First $Players

Add-Type -Namespace MiSideLab -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);
'@

$missing = $labs | Where-Object { -not (Test-Path (Join-Path $modRoot "private/$($_.Dir)/MiSideFull.exe")) } | ForEach-Object { $_.Dir -replace '^lab-', '' }
if ($missing) { & (Join-Path $PSScriptRoot 'make-lab.ps1') -Names $missing }

$logs = Join-Path $modRoot 'private/lab-logs'
New-Item -ItemType Directory -Force $logs | Out-Null
Get-ChildItem $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Clear-Content $_.FullName -ErrorAction SilentlyContinue }
$window = '-screen-fullscreen 0 -screen-width 640 -screen-height 360'
$dll = Join-Path $modRoot 'artifacts/build/MiSideTogether.dll'
$started = @()
foreach ($lab in $labs) {
    $dir = Join-Path $modRoot "private/$($lab.Dir)"
    New-Item -ItemType Directory -Force (Join-Path $dir 'BepInEx/plugins/MiSideTogether') | Out-Null
    Copy-Item -Force $dll (Join-Path $dir 'BepInEx/plugins/MiSideTogether/')
    $log = Join-Path $logs "$($lab.Name).log"
    # Lab port 27130 keeps tests clear of a real game hosting on 27030 at the same time.
    $role = if ($lab.Name -eq 'host') { "-mt-host -mt-port 27130 -mt-name Host -mt-players $Players $HostArgs" } else { "-mt-join $JoinAddress -mt-port 27130 -mt-name $($lab.Name.ToUpper()) $ClientArgs" }
    if ($Tour) { $role += " -mt-tour $Tour" }
    $p = Start-Process -FilePath (Join-Path $dir 'MiSideFull.exe') -WorkingDirectory $dir -PassThru -WindowStyle Minimized `
        -ArgumentList "$window -logFile `"$log`" -mt-lab $role"
    $started += $p
    if (!$Visible) {
        # Hide the window as soon as Unity creates it so the lab never covers the user's screen.
        for ($i = 0; $i -lt 150; $i++) { $p.Refresh(); if ($p.MainWindowHandle -ne [IntPtr]::Zero) { [MiSideLab.Win]::ShowWindow($p.MainWindowHandle, 0) | Out-Null; break }; Start-Sleep -Milliseconds 100 }
    }
    if ($lab.Name -eq 'host') { Start-Sleep 4 }
}
Set-Content (Join-Path $modRoot 'private/lab-pids.txt') (($started | ForEach-Object Id) -join "`n")
Write-Output ("started " + (($started | ForEach-Object { $_.Id }) -join ', '))
if ($Seconds -gt 0) {
    Start-Sleep $Seconds
    foreach ($p in $started) { if (!$p.HasExited) { Stop-Process -Id $p.Id -Force } }
    foreach ($p in $started) { $p.WaitForExit(15000) | Out-Null }
    Write-Output 'stopped lab processes'
}
