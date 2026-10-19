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
    'Scam With Your Friends_Data\UnitySubsystems\UnityOpenXR\UnitySubsystemsManifest.json')
function Expect-Failure([scriptblock]$Action, [string]$Message) {
    $caught = $null
    try { & $Action | Out-Null } catch { $caught = $_.Exception.Message }
    if (!$caught -or $caught -notlike ('*' + $Message + '*')) { throw "Expected '$Message', got '$caught'." }
}
$previousRuntime = $env:XR_RUNTIME_JSON
try {
    foreach ($relative in $relativeFiles) {
        $target = Join-Path $fixture $relative
        [void][IO.Directory]::CreateDirectory((Split-Path $target -Parent))
        [IO.File]::WriteAllText($target, 'fixture')
    }
    Copy-Item -LiteralPath (Join-Path $GameDir 'UnityPlayer.dll') -Destination (Join-Path $fixture 'UnityPlayer.dll') -Force
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
    Write-Output 'Setup checks passed: valid PC/VR layouts, tampered files, path escape, incomplete manifest, mock runtime and missing runtime library.'
} finally {
    $env:XR_RUNTIME_JSON = $previousRuntime
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (!$resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notlike 'swyf-setup-check-*') { throw 'Refusing cleanup outside the temporary test directory.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
