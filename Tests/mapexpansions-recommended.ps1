<#
In-game check: Based runs with every MapExpansions from 1.0.0 on and warns below the recommended 1.0.4.

For each release (1.0.3, 1.0.4) it puts that release's MapExpansions.dll into the slot, starts the game with
-KeepSlot and reads MelonLoader/Latest.log: Based must initialize, and the [Requirements] line must warn for
1.0.3 and pass for 1.0.4. Build the slot once before, with this worktree's Based (-Build takes the folder name
under the workspace root, not a path):

    pwsh -File Workspace/tools/session/session.ps1 -Slot mptest -Build _wt-Based-<topic> -Profile main -NoApp
    pwsh -File <this worktree>/Tests/mapexpansions-recommended.ps1 [-Slot mptest]
#>
param(
    [string]$Slot = 'mptest',
    [string[]]$Versions = @('1.0.3', '1.0.4'),
    # Also start once without Personnel: Based must stay off and name the missing mod.
    [switch]$MissingCase
)
$ErrorActionPreference = 'Stop'
$root = 'F:\Projects\Mods\Schedule1'
$folders = @{ mptest = '_MPTest'; mptest2 = '_MPTest2'; mptest3 = '_MPTest3'; mptest4 = '_MPTest4'; mptest5 = '_MPTest5'; mptest6 = '_MPTest6' }
if (-not $folders.ContainsKey($Slot)) { throw "unknown slot $Slot" }
$game = Join-Path $root "$($folders[$Slot])\Schedule I"
$log = Join-Path $game 'MelonLoader\Latest.log'
$work = Join-Path $env:TEMP "based-mx-recommended"
New-Item -ItemType Directory -Force $work | Out-Null
$pass = 0; $fail = 0

function Check([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { $script:pass++; Write-Host "PASS $name" } else { $script:fail++; Write-Host "FAIL $name :: $detail" }
}

function Stop-Slot {
    Get-Process | Where-Object { $_.Path -like "$game\*" } | ForEach-Object {
        Write-Host "stopping $($_.Id) $($_.Path)"
        Stop-Process -Id $_.Id -Force
        $_.WaitForExit(15000) | Out-Null
    }
}

foreach ($v in $Versions) {
    $dir = Join-Path $work $v
    New-Item -ItemType Directory -Force $dir | Out-Null
    gh release download "v$v" -R DooDesch-Mods/ScheduleOne-MapExpansions -p MapExpansions.dll -D $dir --clobber
    if ($LASTEXITCODE -ne 0) { throw "download of MapExpansions $v failed" }

    Stop-Slot
    $target = Join-Path $game 'Mods\MapExpansions.dll'
    # The slot's DLL can be a hardlink into a build folder; remove the link instead of writing through it.
    if (Test-Path $target) { Remove-Item $target -Force }
    Copy-Item (Join-Path $dir 'MapExpansions.dll') $target
    Write-Host "MapExpansions $v in $target"

    pwsh -File (Join-Path $root 'Workspace\tools\session\session.ps1') -Slot $Slot -KeepSlot -NoApp
    if ($LASTEXITCODE -ne 0) { Check "MapExpansions ${v}: game ready" $false "session.ps1 exit $LASTEXITCODE"; continue }

    $lines = Get-Content $log
    $req = $lines | Where-Object { $_ -match '\[Requirements\] MapExpansions' }
    $req | ForEach-Object { Write-Host "  $_" }
    Check "MapExpansions ${v}: Based initialized" ([bool]($lines | Where-Object { $_ -match 'Based initialized' })) (($lines | Where-Object { $_ -match 'Based stays off' }) -join ' | ')
    $usable = $lines | Where-Object { $_ -match '\[Based\] \[Requirements\] every dependency in thunderstore/manifest\.json is usable: ' }
    $usable | ForEach-Object { Write-Host "  $_" }
    $problems = ($lines | Where-Object { $_ -match '\[Requirements\].*not usable|too old' }) -join ' | '
    Check "MapExpansions ${v}: hard minimum 1.0.0 met" ([bool]($usable | Where-Object { $_ -match 'MapExpansions 1\.0\.0\+' })) $problems
    # The other minimums come from thunderstore/manifest.json.
    $manifest = Get-Content (Join-Path $PSScriptRoot '..\thunderstore\manifest.json') -Raw | ConvertFrom-Json
    foreach ($dep in $manifest.dependencies) {
        if ($dep -notmatch '^[^-]+-([^-]+)-(\d+\.\d+\.\d+)$' -or $Matches[1] -in 'MelonLoader', 'MapExpansions') { continue }
        $name = if ($Matches[1] -eq 'S1API_Forked') { 'S1API \(Forked by Bars\)' } else { [regex]::Escape($Matches[1]) }
        $min = [regex]::Escape($Matches[2])
        Check "MapExpansions ${v}: manifest minimum $dep checked" ([bool]($usable | Where-Object { $_ -match "$name $min\+" })) $problems
    }
    if ([version]$v -lt [version]'1.0.4') {
        Check "MapExpansions ${v}: warns below 1.0.4" ([bool]($req | Where-Object { $_ -match 'older than the recommended 1\.0\.4' })) ($req -join ' | ')
    } else {
        Check "MapExpansions ${v}: meets 1.0.4" ([bool]($req | Where-Object { $_ -match 'meets the recommended 1\.0\.4' })) ($req -join ' | ')
    }
}
if ($MissingCase) {
    # A required mod that is gone keeps Based off and names it; Personnel stands in for every hard requirement.
    Stop-Slot
    $personnel = Join-Path $game 'Mods\Personnel.dll'
    $parked = Join-Path $work 'Personnel.dll'
    if (Test-Path $personnel) { Move-Item $personnel $parked -Force } else { Check "Personnel missing: Personnel.dll was in the slot" $false $personnel }
    try {
        pwsh -File (Join-Path $root 'Workspace\tools\session\session.ps1') -Slot $Slot -KeepSlot -NoApp
        $lines = Get-Content $log
        $off = $lines | Where-Object { $_ -match 'Based stays off: ' }
        $off | ForEach-Object { Write-Host "  $_" }
        Check "Personnel missing: Based stays off and names it" ([bool]($off | Where-Object { $_ -match 'Personnel (is not installed|ist nicht installiert)\.' })) ($off -join ' | ')
        Check "Personnel missing: Based did not initialize" (-not ($lines | Where-Object { $_ -match 'Based initialized' })) 'Based initialized anyway'
    }
    finally {
        Stop-Slot
        if (Test-Path $parked) { Move-Item $parked $personnel -Force; Write-Host "Personnel.dll back in $personnel" }
    }
}
Stop-Slot
Write-Host "===== $pass PASS, $fail FAIL"
exit ([int]($fail -gt 0))
