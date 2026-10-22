#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Scam With Your Friends',
    [Parameter(Mandatory)][string]$UnityEditorDir,
    [Parameter(Mandatory)][string]$Destination,
    [string]$CacheDir
)
$ErrorActionPreference = 'Stop'
if (!$IsWindows) { throw 'Setup requires Windows x64 and PowerShell 7.' }
$repo = Split-Path $PSScriptRoot -Parent
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path.TrimEnd('\')
$UnityEditorDir = (Resolve-Path -LiteralPath $UnityEditorDir).Path.TrimEnd('\')
$Destination = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
if (Test-Path -LiteralPath $Destination) { throw 'Destination already exists. Choose a new game-copy folder.' }
if (Get-CimInstance Win32_Process -Filter "Name='Scam With Your Friends.exe'") { throw 'Close the game before setup.' }
foreach ($relative in @('Scam With Your Friends.exe', 'UnityPlayer.dll', 'Scam With Your Friends_Data\Managed\Assembly-CSharp.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $GameDir $relative) -PathType Leaf)) { throw "Missing source game file: $relative" }
}
foreach ($relative in @('GameAssembly.dll', 'swyf-vr-manifest.json', 'winhttp.dll', 'BepInEx', '.swyf-vr', 'Play VR.cmd', 'Play PC.cmd')) {
    if (Test-Path -LiteralPath (Join-Path $GameDir $relative)) { throw 'Use an unmodified Windows Mono source game folder.' }
}
$profile = & (Join-Path $PSScriptRoot 'Resolve-Dependencies.ps1') -GameDir $GameDir
if (!$CacheDir) { $CacheDir = Join-Path $repo ('work\dependencies\' + $profile.unityVersion) }
$CacheDir = [IO.Path]::GetFullPath($CacheDir).TrimEnd('\')
function Assert-Separate([string]$First, [string]$Second) {
    if ($First.Equals($Second, [StringComparison]::OrdinalIgnoreCase) -or
        $First.StartsWith($Second + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $Second.StartsWith($First + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Game, editor, dependency cache and destination must be separate folders, not inside one another.'
    }
}
foreach ($inputPath in @($GameDir, $UnityEditorDir)) {
    Assert-Separate $Destination $inputPath
    Assert-Separate $CacheDir $inputPath
}
Assert-Separate $CacheDir $Destination
# The preparation cache is writable too. Check its existing parents so a
# junction cannot redirect setup output into an input game/editor tree.
foreach ($path in @($GameDir, $UnityEditorDir, $CacheDir, $Destination, $repo)) {
    $ancestor = $path
    while ($ancestor) {
        if ((Test-Path -LiteralPath $ancestor) -and
            ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Linked folders are unsupported by setup: $ancestor"
        }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
}
$dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
if (!$dotnet) { throw 'Install the .NET 10 SDK, reopen PowerShell 7 and run setup again.' }
$sdk = & $dotnet.Source --version
if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^(\d+)\.' -or [int]$Matches[1] -lt 10) {
    throw 'Setup requires the .NET 10 SDK or later; a runtime-only installation is insufficient.'
}
$stage = Join-Path $repo ('work\setup\' + [guid]::NewGuid().ToString('N') + '\stage')
Write-Host '[1/4] Preparing matching, verified dependencies'
& (Join-Path $PSScriptRoot 'Prepare-Dependencies.ps1') -GameDir $GameDir -UnityEditorDir $UnityEditorDir -CacheDir $CacheDir
Write-Host '[2/4] Building the Release solution'
Push-Location -LiteralPath $repo
try {
    & $dotnet.Source build (Join-Path $repo 'ScamWithYourFriendsInVR.sln') -c Release
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE). No game copy was installed." }
} finally { Pop-Location }
Write-Host '[3/4] Staging the matching runtime'
& (Join-Path $PSScriptRoot 'Stage-Mod.ps1') -Destination $stage
Write-Host '[4/4] Creating and verifying your separate game copy'
& (Join-Path $PSScriptRoot 'Install-GameCopy.ps1') -SourceGameDir $GameDir -StageDir $stage -Destination $Destination
Write-Output "Setup complete. Connect your headset and open '$(Join-Path $Destination 'Play VR.cmd')', or use 'Play PC.cmd' for flat-screen play."
Write-Output "Verified runtime stage retained at '$stage'."
