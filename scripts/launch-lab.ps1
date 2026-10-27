# Starts isolated lab copies (private\lab-host, private\lab-client, ...) as small windows parked down the right edge of
# the primary monitor (never activated, never topmost), at the lowest quality and BelowNormal priority. -Hidden hides them.
# Test-owned PIDs go to private\lab-pids.txt; stop only those (never a user-owned game process).
param(
    [int]$Players = 2, [int]$Seconds = 0, [switch]$Visible, [switch]$Hidden, [switch]$SteamMock,
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
[DllImport("user32.dll")] public static extern bool SetWindowPos(System.IntPtr hWnd, System.IntPtr after, int x, int y, int w, int h, uint flags);
'@

$missing = $labs | Where-Object { -not (Test-Path (Join-Path $modRoot "private/$($_.Dir)/MiSideFull.exe")) } | ForEach-Object { $_.Dir -replace '^lab-', '' }
if ($missing) { & (Join-Path $PSScriptRoot 'make-lab.ps1') -Names $missing }

$logs = Join-Path $modRoot 'private/lab-logs'
New-Item -ItemType Directory -Force $logs | Out-Null
Get-ChildItem $logs -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Clear-Content $_.FullName -ErrorAction SilentlyContinue }
# 480x270 windowed, lowest quality: the other mods' labs share this PC.
$window = '-screen-fullscreen 0 -screen-width 480 -screen-height 270 -screen-quality Fastest'
Add-Type -AssemblyName System.Windows.Forms
$area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$slot = 0
$dll = Join-Path $modRoot 'artifacts/build/MiSideTogether.dll'
# -SteamMock: every copy is a fake Steam account (900000000 + its slot), clients join the host's SteamID the way
# Steam's Join Game does (+mt_join), and every 4th send fails like a full Steam send buffer (src\Network\SteamMock.cs).
$mockDir = Join-Path $modRoot 'private/steam-mock'
if (Test-Path $mockDir) { Remove-Item $mockDir -Recurse -Force }
$mockSlot = 0
$started = @()
foreach ($lab in $labs) {
    $dir = Join-Path $modRoot "private/$($lab.Dir)"
    New-Item -ItemType Directory -Force (Join-Path $dir 'BepInEx/plugins/MiSideTogether') | Out-Null
    Copy-Item -Force $dll (Join-Path $dir 'BepInEx/plugins/MiSideTogether/')
    $log = Join-Path $logs "$($lab.Name).log"
    # Lab port 27130 keeps tests clear of a real game hosting on 27030 at the same time.
    $mockSlot++
    $joinHow = if ($SteamMock) { '+mt_join 900000001' } else { "-mt-join $JoinAddress" }
    $role = if ($lab.Name -eq 'host') { "-mt-host -mt-port 27130 -mt-name Host -mt-players $Players $HostArgs" } else { "$joinHow -mt-port 27130 -mt-name $($lab.Name.ToUpper()) $ClientArgs" }
    if ($SteamMock) { $role += " -mt-steam-mock $mockSlot -mt-steam-fail 4" }
    if ($Tour) { $role += " -mt-tour $Tour" }
    $p = Start-Process -FilePath (Join-Path $dir 'MiSideFull.exe') -WorkingDirectory $dir -PassThru -WindowStyle Minimized `
        -ArgumentList "$window -logFile `"$log`" -mt-lab $role"
    $started += $p
    try { $p.PriorityClass = 'BelowNormal' } catch { }
    if (!$Visible) {
        # As soon as Unity creates the window: hidden, or parked at the right edge, stacked top to bottom
        # (SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE, then SW_SHOWNOACTIVATE). It never takes focus.
        for ($i = 0; $i -lt 150; $i++) {
            $p.Refresh()
            if ($p.MainWindowHandle -ne [IntPtr]::Zero) {
                if ($Hidden) { [MiSideLab.Win]::ShowWindow($p.MainWindowHandle, 0) | Out-Null }
                else {
                    $y = $area.Top + ($slot * 300) % [Math]::Max(300, $area.Height - 300)
                    [MiSideLab.Win]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, $area.Right - 500, $y, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010) | Out-Null
                    [MiSideLab.Win]::ShowWindow($p.MainWindowHandle, 4) | Out-Null
                    $slot++
                }
                break
            }
            Start-Sleep -Milliseconds 100
        }
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
