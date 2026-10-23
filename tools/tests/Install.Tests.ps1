[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDir,
    [Parameter(Mandatory)][string]$StageDir
)
$ErrorActionPreference = 'Stop'
$tools = Split-Path $PSScriptRoot -Parent
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$fixture = Join-Path $tempRoot ('swyf-install-check-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'stock'
$destination = Join-Path $fixture 'installed with spaces'
$badStage = Join-Path $fixture 'bad-stage'
$unused = Join-Path $fixture 'must-not-be-created'
function Expect-Failure([scriptblock]$Action, [string]$Message) {
    $caught = $null
    try { & $Action | Out-Null } catch { $caught = $_.Exception.Message }
    if (!$caught -or $caught -notlike ('*' + $Message + '*')) { throw "Expected '$Message', got '$caught'." }
    if (Test-Path -LiteralPath $unused) { throw 'Invalid inputs created a destination before validation finished.' }
}
function Install([string]$Stage, [string]$Target) {
    & (Join-Path $tools 'Install-GameCopy.ps1') -SourceGameDir $source -StageDir $Stage -Destination $Target
}
function Save-BadManifest($Entries) {
    $Entries | ConvertTo-Json -AsArray | Set-Content -LiteralPath (Join-Path $badStage 'swyf-vr-manifest.json')
}
try {
    [void][IO.Directory]::CreateDirectory((Join-Path $source 'Scam With Your Friends_Data\Managed'))
    [void][IO.Directory]::CreateDirectory($badStage)
    [IO.File]::WriteAllText((Join-Path $source 'Scam With Your Friends.exe'), 'non-executable test fixture')
    [IO.File]::WriteAllText((Join-Path $source 'Scam With Your Friends_Data\Managed\Assembly-CSharp.dll'), 'fixture')
    [IO.File]::WriteAllText((Join-Path $source 'user-settings.keep'), 'preserved original data')
    [void][IO.Directory]::CreateDirectory((Join-Path $source 'Scam With Your Friends_Data\UWBCache'))
    [IO.File]::WriteAllText((Join-Path $source 'Scam With Your Friends_Data\UWBCache\fixture.txt'), 'runtime cache fixture')
    [IO.File]::WriteAllText((Join-Path $source 'Scam With Your Friends_Data\UnityWebBrowser.Engine.Cef.log'), 'runtime output fixture')
    [void][IO.Directory]::CreateDirectory((Join-Path $source 'empty-asset-folder'))
    foreach ($relative in @('UnityPlayer.dll', 'Scam With Your Friends_Data\Managed\Unity.InputSystem.dll')) {
        Copy-Item -LiteralPath (Join-Path $GameDir $relative) -Destination (Join-Path $source $relative)
    }
    # No executable game is present in this fixture, and process enumeration is deterministic.
    function Get-CimInstance { param($ClassName, $Filter) }
    Expect-Failure { Install $StageDir (Join-Path $source 'nested') } 'outside the source'
    Expect-Failure { Install $StageDir $source } 'already exists'
    Save-BadManifest @([pscustomobject]@{path='..\outside.dll';sha256=('0' * 64)})
    Expect-Failure { Install $badStage $unused } 'leaves its folder'
    [IO.File]::WriteAllText((Join-Path $badStage 'fixture.txt'), 'fixture')
    $hash = (Get-FileHash -LiteralPath (Join-Path $badStage 'fixture.txt')).Hash
    Save-BadManifest @([pscustomobject]@{path='fixture.txt';sha256=$hash}, [pscustomobject]@{path='.\FIXTURE.txt';sha256=$hash})
    Expect-Failure { Install $badStage $unused } 'Duplicate'
    Save-BadManifest @([pscustomobject]@{path='fixture.txt';sha256=('0' * 64)})
    Expect-Failure { Install $badStage $unused } 'missing or changed'
    Copy-Item -LiteralPath (Join-Path $StageDir 'swyf-vr-build.json') -Destination $badStage
    $identity = Get-Content -Raw -LiteralPath (Join-Path $badStage 'swyf-vr-build.json') | ConvertFrom-Json
    $identity.unityPlayerSha256 = '0' * 64
    $identity | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $badStage 'swyf-vr-build.json')
    Save-BadManifest @([pscustomobject]@{path='swyf-vr-build.json';sha256=(Get-FileHash -LiteralPath (Join-Path $badStage 'swyf-vr-build.json')).Hash})
    Expect-Failure { Install $badStage $unused } 'does not match'
    function Get-CimInstance { param($ClassName, $Filter) [pscustomobject]@{ProcessId=1234} }
    Expect-Failure { Install $StageDir $unused } 'Close the game'
    function Get-CimInstance { param($ClassName, $Filter) }
    Install $StageDir $destination | Out-Null
    & (Join-Path $tools 'Check-Setup.ps1') -GameDir $destination -Mode PC | Out-Null
    foreach ($mode in @('VR', 'PC')) {
        $launcher = [IO.File]::ReadAllText((Join-Path $destination "Play $mode.cmd"))
        if (!$launcher.Contains('"%~dp0.swyf-vr\Launch-Game.ps1"') -or !$launcher.Contains("-Mode $mode")) { throw 'Generated launcher paths or mode are incorrect.' }
    }
    foreach ($name in @('Launch-Game.ps1', 'Check-Setup.ps1', 'Resolve-Dependencies.ps1', 'Assert-InputVersion.ps1', 'dependencies.lock.json')) {
        if (!(Test-Path -LiteralPath (Join-Path $destination ('.swyf-vr\' + $name)))) { throw 'Installed launcher dependency missing.' }
    }
    $launches = [Collections.Generic.List[object]]::new()
    function Start-Process {
        param($FilePath, $WorkingDirectory, $ArgumentList, [switch]$PassThru)
        $result = [pscustomobject]@{Id=1234;Watched=$false;Disposed=$false}
        $result | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value { param($milliseconds) $this.Watched = $milliseconds -eq 2000; return $false }
        $result | Add-Member -MemberType ScriptMethod -Name Dispose -Value { $this.Disposed = $true }
        $launches.Add([pscustomobject]@{File=$FilePath; Directory=$WorkingDirectory; Args=$ArgumentList;Process=$result})
        return $result
    }
    $installedLaunch = Join-Path $destination '.swyf-vr\Launch-Game.ps1'
    & $installedLaunch -GameDir $destination -Mode PC | Out-Null
    if ($launches.Count -ne 1 -or $launches[0].Directory -ne $destination -or $launches[0].Args -ne '-force-d3d11') {
        throw 'Portable launch tools failed without the source repository.'
    }
    if (!$launches[0].Process.Watched -or !$launches[0].Process.Disposed) { throw 'Portable launcher omitted startup observation or process-handle cleanup.' }
    if ([IO.File]::ReadAllText((Join-Path $destination 'user-settings.keep')) -cne 'preserved original data') { throw 'Game copy omitted original files.' }
    if (!(Test-Path -LiteralPath (Join-Path $destination 'empty-asset-folder') -PathType Container)) { throw 'Game copy omitted an empty original folder.' }
    foreach ($relative in @('Scam With Your Friends_Data\UWBCache', 'Scam With Your Friends_Data\UnityWebBrowser.Engine.Cef.log')) {
        if (Test-Path -LiteralPath (Join-Path $destination $relative)) { throw 'Game copy inherited transient browser data.' }
        if (!(Test-Path -LiteralPath (Join-Path $source $relative))) { throw 'Cache exclusion modified the source.' }
    }
    if (Test-Path -LiteralPath (Join-Path $source 'winhttp.dll')) { throw 'Installer modified the source.' }
    if (Test-Path -LiteralPath (Join-Path $source 'swyf-vr-manifest.json')) { throw 'Installer modified the source manifest.' }
    Write-Output 'Install checks passed: verified full stage, untouched source, preserved files/empty folders, fresh browser state, portable PC/VR launchers, mismatched builds, corrupt files, duplicate/traversal paths, nested/existing destinations and active game guard. No game was launched.'
} finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (!$resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notlike 'swyf-install-check-*') { throw 'Refusing cleanup outside the temporary test directory.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
