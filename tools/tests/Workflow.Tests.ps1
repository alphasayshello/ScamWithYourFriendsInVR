#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$tools = Split-Path $PSScriptRoot -Parent
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$fixture = Join-Path $tempRoot ('swyf-workflow-' + [guid]::NewGuid().ToString('N'))
$repo = Join-Path $fixture 'source with spaces'
$fixtureTools = Join-Path $repo 'tools'
$source = Join-Path $fixture 'stock game'
$editor = Join-Path $fixture 'unity editor'
$cache = Join-Path $fixture 'dependency cache'
$destination = Join-Path $fixture 'installed game'
$calls = Join-Path $repo 'calls.txt'
$fakeDotnet = Join-Path $repo 'fake-dotnet.ps1'
$link = Join-Path $fixture 'cache-link'
$fakeRunning = $false
$missingSdk = $false
function Get-CimInstance { param($ClassName, $Filter) if ($fakeRunning) { [pscustomobject]@{ProcessId=123} } }
function Get-Command {
    param($Name, $CommandType, $ErrorAction)
    if ($Name -eq 'dotnet' -and !$missingSdk) { [pscustomobject]@{Source=$fakeDotnet} }
}
function Run-Setup([string]$Target=$destination, [string]$Dependencies=$cache) {
    & (Join-Path $fixtureTools 'Setup-Mod.ps1') -GameDir $source -UnityEditorDir $editor -Destination $Target -CacheDir $Dependencies
}
function Reset-Calls { if (Test-Path -LiteralPath $calls) { [IO.File]::WriteAllText($calls, '') } }
function Expect-Failure([scriptblock]$Action, [string]$Message, [string]$ExpectedCalls='') {
    Reset-Calls
    $caught = $null
    try { & $Action | Out-Null } catch { $caught = $_.Exception.Message }
    if (!$caught -or !$caught.Contains($Message)) { throw "Expected '$Message', got '$caught'." }
    $actual = if (Test-Path -LiteralPath $calls) { (Get-Content -LiteralPath $calls) -join ',' } else { '' }
    if ($actual -ne $ExpectedCalls) { throw "Unexpected setup steps: $actual" }
    if (Test-Path -LiteralPath $destination) { throw 'Failed setup created a game destination.' }
}
try {
    foreach ($directory in @($fixtureTools, $source, $editor, (Join-Path $source 'Scam With Your Friends_Data\Managed'))) {
        [void][IO.Directory]::CreateDirectory($directory)
    }
    Copy-Item -LiteralPath (Join-Path $tools 'Setup-Mod.ps1') -Destination $fixtureTools
    foreach ($relative in @('Scam With Your Friends.exe', 'UnityPlayer.dll', 'Scam With Your Friends_Data\Managed\Assembly-CSharp.dll')) {
        [IO.File]::WriteAllText((Join-Path $source $relative), 'non-executable input fixture')
    }
    Set-Content -LiteralPath (Join-Path $fixtureTools 'Resolve-Dependencies.ps1') -Value 'param($GameDir) [pscustomobject]@{unityVersion="6000.3.26f1"}'
    @'
param($GameDir, $UnityEditorDir, $CacheDir)
Add-Content -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'calls.txt') -Value 'prepare'
'@ | Set-Content -LiteralPath (Join-Path $fixtureTools 'Prepare-Dependencies.ps1')
    @'
param($Destination)
Add-Content -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'calls.txt') -Value 'stage'
[void][IO.Directory]::CreateDirectory($Destination)
'@ | Set-Content -LiteralPath (Join-Path $fixtureTools 'Stage-Mod.ps1')
    @'
param($SourceGameDir, $StageDir, $Destination)
if (!(Test-Path -LiteralPath $StageDir)) { throw 'Installer received no stage.' }
Add-Content -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'calls.txt') -Value 'install'
[void][IO.Directory]::CreateDirectory($Destination)
'@ | Set-Content -LiteralPath (Join-Path $fixtureTools 'Install-GameCopy.ps1')
    @'
if ($args[0] -eq '--version') {
    Get-Content -LiteralPath (Join-Path $PSScriptRoot 'sdk.txt')
    $global:LASTEXITCODE = 0
    return
}
if ($args[0] -ne 'build' -or $args[2] -ne '-c' -or $args[3] -ne 'Release') { throw 'Unexpected build arguments.' }
Add-Content -LiteralPath (Join-Path $PSScriptRoot 'calls.txt') -Value 'build'
$global:LASTEXITCODE = [int](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'build-exit.txt'))
'@ | Set-Content -LiteralPath $fakeDotnet
    Set-Content -LiteralPath (Join-Path $repo 'sdk.txt') -Value '10.0.100'
    Set-Content -LiteralPath (Join-Path $repo 'build-exit.txt') -Value '0'
    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $source 'UnityPlayer.dll')).Hash
    Expect-Failure { Run-Setup (Join-Path $source 'nested') } 'must be separate'
    Expect-Failure { Run-Setup $destination (Join-Path $source 'cache') } 'must be separate'
    $fakeRunning = $true
    Expect-Failure { Run-Setup } 'Close the game'
    $fakeRunning = $false
    $missingSdk = $true
    Expect-Failure { Run-Setup } 'Install the .NET 10 SDK'
    $missingSdk = $false
    Set-Content -LiteralPath (Join-Path $repo 'sdk.txt') -Value '9.0.100'
    Expect-Failure { Run-Setup } 'requires the .NET 10 SDK'
    Set-Content -LiteralPath (Join-Path $repo 'sdk.txt') -Value '10.0.100'
    [void][IO.Directory]::CreateDirectory((Join-Path $source 'BepInEx'))
    Expect-Failure { Run-Setup } 'unmodified Windows Mono'
    Remove-Item -LiteralPath (Join-Path $source 'BepInEx')
    [void](New-Item -ItemType Junction -Path $link -Target $source)
    Expect-Failure { Run-Setup $destination (Join-Path $link 'cache') } 'Linked folders are unsupported'
    Remove-Item -LiteralPath $link -Force
    Set-Content -LiteralPath (Join-Path $repo 'build-exit.txt') -Value '23'
    Expect-Failure { Run-Setup } 'Build failed (exit 23)' 'prepare,build'
    Set-Content -LiteralPath (Join-Path $repo 'build-exit.txt') -Value '0'
    Reset-Calls
    $before = (Get-Location).Path
    Run-Setup | Out-Null
    if (((Get-Content -LiteralPath $calls) -join ',') -ne 'prepare,build,stage,install') { throw 'Setup did not run the complete workflow in order.' }
    if ((Get-Location).Path -ne $before) { throw 'Setup changed the caller working directory.' }
    if ((Get-FileHash -LiteralPath (Join-Path $source 'UnityPlayer.dll')).Hash -ne $sourceHash) { throw 'Setup changed the source game.' }
    if (!(Test-Path -LiteralPath $destination)) { throw 'Successful setup did not create its destination.' }
    $caught = $null
    try { Run-Setup | Out-Null } catch { $caught = $_.Exception.Message }
    if (!$caught -or !$caught.Contains('already exists')) { throw 'Setup accepted an existing destination.' }
    Write-Output 'SETUP WORKFLOW PASS: ordered preparation/build/staging/install, paths with spaces, early input/SDK/process rejection, linked cache rejection and build-failure stop'
} finally {
    if (Test-Path -LiteralPath $link) { Remove-Item -LiteralPath $link -Force }
    $checked = [IO.Path]::GetFullPath($fixture)
    if (!$checked.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or $checked -eq $tempRoot.TrimEnd('\')) { throw 'Unexpected fixture cleanup path.' }
    if (Test-Path -LiteralPath $checked) { Remove-Item -LiteralPath $checked -Recurse -Force }
}
