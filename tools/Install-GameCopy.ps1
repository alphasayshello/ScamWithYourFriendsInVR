[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceGameDir,
    [Parameter(Mandatory)][string]$StageDir,
    [Parameter(Mandatory)][string]$Destination
)
$ErrorActionPreference = 'Stop'
$SourceGameDir = (Resolve-Path -LiteralPath $SourceGameDir).Path.TrimEnd('\')
$StageDir = (Resolve-Path -LiteralPath $StageDir).Path.TrimEnd('\')
$Destination = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
if ($SourceGameDir.Equals($StageDir, [StringComparison]::OrdinalIgnoreCase) -or
    $StageDir.StartsWith($SourceGameDir + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $SourceGameDir.StartsWith($StageDir + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Source and stage must be separate folders.' }
if (Test-Path -LiteralPath $Destination) { throw 'Destination already exists. Choose a new game-copy folder.' }
foreach ($root in @($SourceGameDir, $StageDir)) {
    if ($Destination.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Destination must be outside the source and stage folders.' }
}
if (Get-CimInstance Win32_Process -Filter "Name='Scam With Your Friends.exe'") { throw 'Close the game before installing.' }
foreach ($relative in @('Scam With Your Friends.exe', 'UnityPlayer.dll', 'Scam With Your Friends_Data\Managed\Assembly-CSharp.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $SourceGameDir $relative) -PathType Leaf)) { throw "Missing source game file: $relative" }
}
foreach ($relative in @('GameAssembly.dll', 'swyf-vr-manifest.json', 'winhttp.dll', 'BepInEx', '.swyf-vr', 'Play VR.cmd', 'Play PC.cmd')) {
    if (Test-Path -LiteralPath (Join-Path $SourceGameDir $relative)) { throw 'Use an unmodified Windows Mono source game folder.' }
}
$profile = & (Join-Path $PSScriptRoot 'Resolve-Dependencies.ps1') -GameDir $SourceGameDir
& (Join-Path $PSScriptRoot 'Assert-InputVersion.ps1') -GameDir $SourceGameDir -ExpectedVersion ($profile.packages | Where-Object name -eq 'com.unity.inputsystem').version
# Never follow junctions out of either input tree while copying or hashing.
foreach ($root in @($SourceGameDir, $StageDir)) {
    if ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked input folders are unsupported.' }
    if (Get-ChildItem -LiteralPath $root -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } | Select-Object -First 1) {
        throw 'Linked files or folders are unsupported. Use a regular game copy and stage.'
    }
}
$manifestPath = Join-Path $StageDir 'swyf-vr-manifest.json'
if (!(Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Missing stage manifest. Run Stage-Mod.ps1 first.' }
$entries = @(Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json)
if ($entries.Count -eq 0) { throw 'The stage manifest is empty.' }
$seen = @{}
foreach ($entry in $entries) {
    $relative = [string]$entry.path
    if (!$relative -or [IO.Path]::IsPathRooted($relative) -or $relative -match '[:*?]') { throw 'Invalid stage manifest path.' }
    $path = [IO.Path]::GetFullPath((Join-Path $StageDir $relative))
    if (!$path.StartsWith($StageDir + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Stage manifest path leaves its folder.' }
    $key = [IO.Path]::GetRelativePath($StageDir, $path)
    if ($seen.ContainsKey($key)) { throw 'Duplicate stage manifest path.' }
    $seen[$key] = $true
    if ($key -eq 'swyf-vr-manifest.json' -or $key -eq 'UnityPlayer.dll' -or $key -eq 'Scam With Your Friends.exe' -or
        $key -like '.swyf-vr*' -or $key -like 'Play *.cmd' -or $key -like 'BepInEx\config\*' -or
        $key -like '*SmokeTests*') { throw "Unexpected staged file: $key" }
    if (!(Test-Path -LiteralPath $path -PathType Leaf) -or [string]$entry.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Staged file is missing or changed: $relative" }
    $entry.path = $key
}
if (!$seen.ContainsKey('swyf-vr-build.json')) { throw 'Stage has no build identity. Re-run Stage-Mod.ps1 with current tools.' }
$build = Get-Content -Raw -LiteralPath (Join-Path $StageDir 'swyf-vr-build.json') | ConvertFrom-Json
if ($build.schemaVersion -ne 1 -or $build.unityVersion -ne $profile.unityVersion -or
    $build.inputSystemVersion -ne ($profile.packages | Where-Object name -eq 'com.unity.inputsystem').version -or
    $build.unityPlayerSha256 -ne (Get-FileHash -LiteralPath (Join-Path $SourceGameDir 'UnityPlayer.dll') -Algorithm SHA256).Hash) {
    throw 'Stage does not match this source game. Prepare, build and stage for the same version.'
}
Write-Output "Copying the game to $Destination. The original stays untouched."
[void][IO.Directory]::CreateDirectory($Destination)
try {
    Get-ChildItem -LiteralPath $SourceGameDir -Force | Copy-Item -Destination $Destination -Recurse -Force
    foreach ($entry in $entries) {
        $target = Join-Path $Destination $entry.path
        [void][IO.Directory]::CreateDirectory((Split-Path $target -Parent))
        Copy-Item -LiteralPath (Join-Path $StageDir $entry.path) -Destination $target -Force
    }
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $Destination 'swyf-vr-manifest.json')
    & (Join-Path $PSScriptRoot 'Check-Setup.ps1') -GameDir $Destination -Mode PC | Out-Null
    $launchTools = Join-Path $Destination '.swyf-vr'
    [void][IO.Directory]::CreateDirectory($launchTools)
    foreach ($name in @('Launch-Game.ps1', 'Check-Setup.ps1', 'Resolve-Dependencies.ps1', 'Assert-InputVersion.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $launchTools
    }
    Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'dependencies*.lock.json' -File | Copy-Item -Destination $launchTools
    foreach ($mode in @('VR', 'PC')) {
        $command = '@echo off' + "`r`n" + 'pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0.swyf-vr\Launch-Game.ps1" -GameDir "%~dp0." -Mode ' + $mode + "`r`nif errorlevel 1 pause`r`n"
        [IO.File]::WriteAllText((Join-Path $Destination "Play $mode.cmd"), $command, [Text.Encoding]::ASCII)
    }
    Write-Output "Installed and verified $($entries.Count) runtime files. Open Play VR.cmd or Play PC.cmd in the new folder."
} catch {
    throw "Installation did not finish. The original game is unchanged; the incomplete copy is at '$Destination'. $($_.Exception.Message)"
}
