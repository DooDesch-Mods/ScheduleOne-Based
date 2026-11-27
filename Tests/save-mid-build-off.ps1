<#
Steps 3 and 4 of Tests/save-mid-build.py: the saved construction with MapExpansions gone, then with it back.

Parks the slot's Mods/MapExpansions.dll, starts the game on the kept save 5 and runs `save-mid-build.py off`. Then it
puts the DLL back, starts again and runs `save-mid-build.py read`, which finds the build where it was saved. Needs the
write step first and a slot built with this worktree's Debug Based:

    pwsh -File <this worktree>/Tests/save-mid-build-off.ps1 -Slot mptest2 -SteamId <id> [-BrokenCase] [-OffOnly]

-BrokenCase also starts once with a MapExpansions.dll in Mods that MelonLoader cannot load (a file of zeros): the
missing-mods line must say the file is in the Mods folder and did not load, not that it is missing or misplaced.
#>
param(
    [string]$Slot = 'mptest2',
    [Parameter(Mandatory)] [string]$SteamId,
    [switch]$BrokenCase,
    # Skips the start without MapExpansions.
    [switch]$NoMissingCase,
    # Skips the start with MapExpansions back.
    [switch]$OffOnly
)
$ErrorActionPreference = 'Stop'
$root = 'F:\Projects\Mods\Schedule1'
$folders = @{ mptest = '_MPTest'; mptest2 = '_MPTest2'; mptest3 = '_MPTest3'; mptest4 = '_MPTest4'; mptest5 = '_MPTest5'; mptest6 = '_MPTest6' }
if (-not $folders.ContainsKey($Slot)) { throw "unknown slot $Slot" }
$game = Join-Path $root "$($folders[$Slot])\Schedule I"
$log = Join-Path $game 'MelonLoader\Latest.log'
$session = Join-Path $root 'Workspace\tools\session\session.ps1'
$check = Join-Path $PSScriptRoot 'save-mid-build.py'
$work = Join-Path $env:TEMP 'based-save-mid-build'
New-Item -ItemType Directory -Force $work | Out-Null
$failed = 0

function Stop-Slot {
    Get-Process | Where-Object { $_.Path -like "$game\*" } | ForEach-Object {
        Write-Host "stopping $($_.Id) $($_.Path)"
        Stop-Process -Id $_.Id -Force
        $_.WaitForExit(15000) | Out-Null
    }
}

function Start-Slot([string]$purpose) {
    pwsh -File $session -Slot $Slot -KeepSlot -NoApp -Template none -Purpose $purpose
    if ($LASTEXITCODE -ne 0) { Write-Host "FAIL  game ready ($purpose) - session.ps1 exit $LASTEXITCODE"; $script:failed++; return $false }
    return $true
}

# The notice lasts 15 s and session.ps1 returns later than that, so a job takes the screenshot as soon as the log
# names the notice. The slot's Latest.log of the last start is removed first so its notice line does not count.
function Start-NoticeShot([string]$name) {
    if (Test-Path $log) { Remove-Item $log -Force; Write-Host "removed the previous $log" }
    $target = Join-Path $PSScriptRoot "out\$name.png"
    Start-Job -ArgumentList $log, $Slot, $target, (Join-Path $root 'Workspace\tools\bridge\s1.py') {
        param($log, $slot, $target, $s1)
        $deadline = (Get-Date).AddMinutes(8)
        while ((Get-Date) -lt $deadline) {
            if ((Test-Path $log) -and (Select-String -Path $log -Pattern 'Construction notice shown' -Quiet)) {
                # One second for the notice to slide in.
                Start-Sleep -Seconds 1
                $asked = Get-Date
                $path = (python $s1 --slot $slot shot | Select-Object -Last 1)
                if (-not $path -or -not (Test-Path $path.Trim())) { return "FAIL  notice-shot  - screenshot failed: $path" }
                # The bridge names a shot after the frame number, so a file of an earlier start can carry the same name
                # and is reported before the new capture overwrites it; wait for a file written after the request.
                $path = $path.Trim()
                $until = (Get-Date).AddSeconds(5)
                while ((Get-Item $path).LastWriteTime -lt $asked.AddSeconds(-1) -and (Get-Date) -lt $until) { Start-Sleep -Milliseconds 200 }
                if ((Get-Item $path).LastWriteTime -lt $asked.AddSeconds(-1)) { return "FAIL  notice-shot  - $path is from an earlier start ($((Get-Item $path).LastWriteTime)), no new capture within 5 s" }
                New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
                Copy-Item $path.Trim() $target -Force
                return "notice screenshot: $target"
            }
            Start-Sleep -Milliseconds 500
        }
        return "FAIL  notice-shot  - no 'Construction notice shown' line within 8 min"
    }
}

function Invoke-Check([string]$mode) {
    python $check --slot $Slot --steamid $SteamId $mode
    if ($LASTEXITCODE -ne 0) { $script:failed++ }
}

# Runs `off` with the slot's MapExpansions.dll parked; $replace, when set, puts a stand-in file in its place.
function Test-Without([string]$purpose, [scriptblock]$replace, [scriptblock]$extra) {
    $dll = Join-Path $game 'Mods\MapExpansions.dll'
    $parked = Join-Path $work 'MapExpansions.dll'
    Stop-Slot
    if (-not (Test-Path $dll)) { throw "$dll is not in the slot; build the slot with MapExpansions first" }
    # The slot's DLL can be a hardlink into a build folder; moving it moves the link, the build output stays as it is.
    Move-Item $dll $parked -Force
    Write-Host "MapExpansions.dll parked in $parked"
    try {
        if ($replace) { & $replace $dll }
        $shot = Start-NoticeShot ('save-mid-build-notice-' + ($purpose -replace '[^A-Za-z]+', '-').Trim('-').ToLower())
        if (Start-Slot $purpose) {
            Invoke-Check 'off'
            if ($extra) { & $extra }
        }
        $said = Receive-Job $shot -Wait -AutoRemoveJob
        Write-Host $said
        if ("$said" -match '^FAIL') { $script:failed++ }
    }
    finally {
        Stop-Slot
        if (Test-Path $dll) { Remove-Item $dll -Force }
        Move-Item $parked $dll -Force
        Write-Host "MapExpansions.dll back in $dll"
    }
}

if (-not $NoMissingCase) { Test-Without 'Based#132 without MapExpansions' $null $null }
if ($BrokenCase) {
    Test-Without 'Based#132 broken MapExpansions' { param($dll) [IO.File]::WriteAllBytes($dll, [byte[]]::new(4096)); Write-Host "zero-filled stand-in at $dll" } {
        $off = Get-Content $log | Where-Object { $_ -match 'Based stays off: ' }
        $ok = [bool]($off | Where-Object { $_ -match 'MapExpansions (is in the Mods folder but did not load|liegt im Mods-Ordner, wurde aber nicht geladen)' })
        if ($ok) { Write-Host "PASS  broken-named  - the line says the file is in Mods and did not load" }
        else { Write-Host "FAIL  broken-named  - $($off -join ' | ')"; $script:failed++ }
    }
}
if (-not $OffOnly -and (Start-Slot 'Based#132 MapExpansions back')) { Invoke-Check 'read' }
Write-Host "===== save-mid-build-off: $failed failing step(s)"
exit ([int]($failed -gt 0))
