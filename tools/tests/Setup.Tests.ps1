[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameDir)
$ErrorActionPreference = 'Stop'
$tools = Split-Path $PSScriptRoot -Parent
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$fixture = Join-Path $tempRoot ('swyf-setup-check-' + [guid]::NewGuid().ToString('N'))
$manifest = Join-Path $fixture 'swyf-vr-manifest.json'
$relativeFiles = @('Scam With Your Friends.exe', 'UnityPlayer.dll', 'winhttp.dll', 'BepInEx\core\BepInEx.dll',
    'BepInEx\plugins\ScamWithYourFriendsInVR\ScamWithYourFriendsInVR.dll',
    'BepInEx\plugins\ScamWithYourFriendsInVR\ScamWithYourFriendsInVR.Runtime.dll',
    'BepInEx\plugins\ScamWithYourFriendsInVR\ScamWithYourFriendsInVR.Multiplayer.dll',
    'Scam With Your Friends_Data\Managed\UnityEngine.XRModule.dll',
    'Scam With Your Friends_Data\Plugins\x86_64\UnityOpenXR.dll',
    'Scam With Your Friends_Data\UnitySubsystems\UnityOpenXR\UnitySubsystemsManifest.json',
    'Scam With Your Friends_Data\Managed\Unity.InputSystem.dll')
function Expect-Failure([scriptblock]$Action, [string]$Message) {
    $caught = $null
    try { & $Action | Out-Null } catch { $caught = $_.Exception.Message }
    if (!$caught -or $caught -notlike ('*' + $Message + '*')) { throw "Expected '$Message', got '$caught'." }
}
$previousRuntime = $env:XR_RUNTIME_JSON
$previousDisable = $env:DOORSTOP_DISABLE
$previousEnabled = $env:DOORSTOP_ENABLED
try {
    foreach ($relative in $relativeFiles) {
        $target = Join-Path $fixture $relative
        [void][IO.Directory]::CreateDirectory((Split-Path $target -Parent))
        [IO.File]::WriteAllText($target, 'fixture')
    }
    Copy-Item -LiteralPath (Join-Path $GameDir 'UnityPlayer.dll') -Destination (Join-Path $fixture 'UnityPlayer.dll') -Force
    Copy-Item -LiteralPath (Join-Path $GameDir $relativeFiles[-1]) -Destination (Join-Path $fixture $relativeFiles[-1]) -Force
    Expect-Failure { & (Join-Path $tools 'Assert-InputVersion.ps1') -GameDir $fixture -ExpectedVersion '99.0.0' } 'Input System version mismatch'
    $entries = @($relativeFiles | Select-Object -Skip 2 | ForEach-Object { [pscustomobject]@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $fixture $_)).Hash} })
    $entries | ConvertTo-Json | Set-Content -LiteralPath $manifest
    $result = & (Join-Path $tools 'Check-Setup.ps1') -GameDir $fixture -Mode PC
    if ($result.VerifiedFiles -ne $entries.Count -or $result.OpenXRRuntime) { throw 'PC setup check did not validate the manifest independently of XR.' }
    $plugin = Join-Path $fixture $relativeFiles[4]
    [IO.File]::WriteAllText($plugin, 'changed')
    Expect-Failure { & (Join-Path $tools 'Check-Setup.ps1') -GameDir $fixture -Mode PC } 'Staged file changed'
    [IO.File]::WriteAllText($plugin, 'fixture')
    @([pscustomobject]@{path='..\outside.dll';sha256=('0' * 64)}) | ConvertTo-Json | Set-Content -LiteralPath $manifest
    Expect-Failure { & (Join-Path $tools 'Check-Setup.ps1') -GameDir $fixture -Mode PC } 'leaves the game folder'
    $entries | Select-Object -Skip 1 | ConvertTo-Json | Set-Content -LiteralPath $manifest
    Expect-Failure { & (Join-Path $tools 'Check-Setup.ps1') -GameDir $fixture -Mode PC } 'does not cover'
    $entries | ConvertTo-Json | Set-Content -LiteralPath $manifest
    $env:XR_RUNTIME_JSON = Join-Path $fixture 'mock-runtime.json'
    [IO.File]::WriteAllText($env:XR_RUNTIME_JSON, '{}')
    Expect-Failure { & (Join-Path $tools 'Check-Setup.ps1') -GameDir $fixture -Mode VR } 'mock OpenXR runtime'
    $env:XR_RUNTIME_JSON = Join-Path $fixture 'runtime.json'
    [IO.File]::WriteAllText($env:XR_RUNTIME_JSON, '{"runtime":{"library_path":"missing.dll"}}')
    Expect-Failure { & (Join-Path $tools 'Check-Setup.ps1') -GameDir $fixture -Mode VR } 'runtime library is missing'
    [IO.File]::WriteAllText($env:XR_RUNTIME_JSON, '{"runtime":{"library_path":"UnityPlayer.dll"}}')
    $result = & (Join-Path $tools 'Check-Setup.ps1') -GameDir $fixture -Mode VR
    if (!$result.OpenXRRuntime) { throw 'VR setup omitted runtime resolution.' }
    # Intercept process APIs: only exercise launcher configuration, never start a game.
    $launches = [Collections.Generic.List[object]]::new()
    $launchProcesses = [Collections.Generic.List[object]]::new()
    $launchScenario = [pscustomobject]@{ExitEarly=$false;ExitCode=0;NativeExit=$false}
    function Get-CimInstance { param($ClassName, $Filter) }
    function Start-Process {
        param($FilePath, $WorkingDirectory, $ArgumentList, [switch]$PassThru)
        $launches.Add([pscustomobject]@{File=$FilePath; Directory=$WorkingDirectory; Args=$ArgumentList})
        if ($launchScenario.NativeExit) {
            return Microsoft.PowerShell.Management\Start-Process -FilePath $env:ComSpec -ArgumentList '/d /c exit 53' -WindowStyle Hidden -PassThru
        }
        $result = [pscustomobject]@{Id=1234;ExitEarly=$launchScenario.ExitEarly;ExitCode=$launchScenario.ExitCode;Disposed=$false}
        $result | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value {
            param($milliseconds)
            if ($milliseconds -ne 2000) { throw 'Unexpected startup observation window.' }
            return $this.ExitEarly
        }
        $result | Add-Member -MemberType ScriptMethod -Name Dispose -Value { $this.Disposed = $true }
        $launchProcesses.Add($result)
        return $result
    }
    $env:DOORSTOP_DISABLE = $null; $env:DOORSTOP_ENABLED = $null
    $configPath = Join-Path $fixture 'BepInEx\config\com.swyf.vr.cfg'
    [void][IO.Directory]::CreateDirectory((Split-Path $configPath -Parent))
    $originalConfig = "[General]`r`nMode = VR`r`nSendPoses = true`r`n`r`n[Other]`r`nMode = untouched`r`n"
    [IO.File]::WriteAllText($configPath, $originalConfig)
    & (Join-Path $tools 'Launch-Game.ps1') -GameDir $fixture -Mode PC | Out-Null
    $updated = [IO.File]::ReadAllText($configPath)
    if ($updated -notmatch 'Mode = PC' -or $updated -notmatch 'Mode = untouched' -or $updated -notmatch 'SendPoses = true') { throw 'Launcher did not preserve unrelated config.' }
    if ([IO.File]::ReadAllText($configPath + '.before-launch.bak') -cne $originalConfig) { throw 'Launcher config backup differs from the original.' }
    & (Join-Path $tools 'Launch-Game.ps1') -GameDir $fixture -Mode VR | Out-Null
    if ([IO.File]::ReadAllText($configPath) -notmatch 'Mode = VR' -or $launches.Count -ne 2 -or $launches[0].Args -ne '-force-d3d11') { throw 'Launcher mode or DirectX selection failed.' }
    if ($launchProcesses | Where-Object { !$_.Disposed }) { throw 'Launcher retained a process handle after its startup observation.' }
    $launchScenario.ExitEarly = $true; $launchScenario.ExitCode = 53
    Expect-Failure { & (Join-Path $tools 'Launch-Game.ps1') -GameDir $fixture -Mode VR } 'exit code 53'
    if (!$launchProcesses[-1].Disposed) { throw 'Launcher retained the failed process handle.' }
    $launchScenario.ExitCode = 0
    Expect-Failure { & (Join-Path $tools 'Launch-Game.ps1') -GameDir $fixture -Mode PC } 'exit code 0'
    $launchScenario.NativeExit = $true
    Expect-Failure { & (Join-Path $tools 'Launch-Game.ps1') -GameDir $fixture -Mode VR } 'exit code 53'
    $launchScenario.NativeExit = $false; $launchScenario.ExitEarly = $false
    $testPlugin = Join-Path $fixture 'BepInEx\plugins\ScamWithYourFriendsInVR\ScamWithYourFriendsInVR.SmokeTests.dll'
    [IO.File]::WriteAllText($testPlugin, 'fixture')
    Expect-Failure { & (Join-Path $tools 'Launch-Game.ps1') -GameDir $fixture -Mode PC } 'smoke-test plugin'
    if ($launches.Count -ne 5) { throw 'Launcher ignored the test-plugin guard.' }
    Write-Output 'Setup checks passed: PC/VR layouts, corrupted files, manifest containment/coverage, runtime selection, launcher modes, config preservation/backups, early native exit reporting, process-handle cleanup and test-plugin guard. No game was launched.'
} finally {
    $env:XR_RUNTIME_JSON = $previousRuntime
    $env:DOORSTOP_DISABLE = $previousDisable
    $env:DOORSTOP_ENABLED = $previousEnabled
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (!$resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notlike 'swyf-setup-check-*') { throw 'Refusing cleanup outside the temporary test directory.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
