[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDir,
    [ValidateSet('VR','PC')][string]$Mode = 'VR'
)
$ErrorActionPreference = 'Stop'
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$required = @('Scam With Your Friends.exe', 'UnityPlayer.dll', 'winhttp.dll',
    'BepInEx\core\BepInEx.dll',
    'BepInEx\plugins\ScamWithYourFriendsInVR\ScamWithYourFriendsInVR.dll',
    'BepInEx\plugins\ScamWithYourFriendsInVR\ScamWithYourFriendsInVR.Runtime.dll',
    'BepInEx\plugins\ScamWithYourFriendsInVR\ScamWithYourFriendsInVR.Multiplayer.dll',
    'Scam With Your Friends_Data\Managed\UnityEngine.XRModule.dll',
    'Scam With Your Friends_Data\Plugins\x86_64\UnityOpenXR.dll',
    'Scam With Your Friends_Data\UnitySubsystems\UnityOpenXR\UnitySubsystemsManifest.json')
foreach ($relative in $required) {
    if (!(Test-Path -LiteralPath (Join-Path $GameDir $relative) -PathType Leaf)) { throw "Missing $relative. Install the complete staged runtime; a plugin-only install is insufficient." }
}
if (Test-Path -LiteralPath (Join-Path $GameDir 'GameAssembly.dll')) { throw 'IL2CPP is unsupported. Use the Windows x64 Mono game.' }
$lock = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'dependencies.lock.json') | ConvertFrom-Json
$version = (Get-Item -LiteralPath (Join-Path $GameDir 'UnityPlayer.dll')).VersionInfo.ProductVersion
if ($version -notmatch ('^' + [regex]::Escape($lock.unityVersion) + '([_\s]|$)')) { throw "Expected Unity $($lock.unityVersion); found $version." }
$manifest = Join-Path $GameDir 'swyf-vr-manifest.json'
if (!(Test-Path -LiteralPath $manifest -PathType Leaf)) { throw 'Missing swyf-vr-manifest.json. Reinstall a complete stage with its manifest.' }
$entries = @(Get-Content -Raw -LiteralPath $manifest | ConvertFrom-Json)
if ($entries.Count -eq 0) { throw 'The runtime manifest is empty.' }
$seen = @{}
foreach ($entry in $entries) {
    $relative = [string]$entry.path
    if (!$relative -or [IO.Path]::IsPathRooted($relative)) { throw 'Invalid runtime manifest path.' }
    $target = [IO.Path]::GetFullPath((Join-Path $GameDir $relative))
    if (!$target.StartsWith($GameDir.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime manifest path leaves the game folder.' }
    if ($seen.ContainsKey($relative)) { throw "Duplicate runtime manifest entry: $relative" }
    $seen[$relative] = $true
    if (!(Test-Path -LiteralPath $target -PathType Leaf)) { throw "Missing staged file: $relative" }
    if ([string]$entry.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.sha256) {
        throw "Staged file changed: $relative. Rebuild/reinstall the complete stage, or check your intended mod update."
    }
}
foreach ($relative in $required | Select-Object -Skip 2) {
    if (!$seen.ContainsKey($relative)) { throw "Runtime manifest does not cover $relative." }
}
$runtime = $null
if ($Mode -eq 'VR') {
    $runtime = $env:XR_RUNTIME_JSON
    if (!$runtime) { $runtime = (Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Khronos\OpenXR\1' -Name ActiveRuntime -ErrorAction SilentlyContinue).ActiveRuntime }
    if (!$runtime -or !(Test-Path -LiteralPath $runtime -PathType Leaf)) { throw 'No PC OpenXR runtime found. Select SteamVR or Meta as the active OpenXR runtime first.' }
    if ($runtime -match '(?i)mock') { throw 'A mock OpenXR runtime is selected. Clear XR_RUNTIME_JSON before normal headset play.' }
    $runtimeInfo = Get-Content -Raw -LiteralPath $runtime | ConvertFrom-Json
    if (!$runtimeInfo.runtime.library_path) { throw 'The selected OpenXR runtime manifest is invalid.' }
    $library = [string]$runtimeInfo.runtime.library_path
    if (![IO.Path]::IsPathRooted($library)) { $library = Join-Path (Split-Path $runtime -Parent) $library }
    if (!(Test-Path -LiteralPath $library -PathType Leaf)) { throw 'The selected OpenXR runtime library is missing. Repair your runtime installation.' }
}
[pscustomobject]@{ GameDir=$GameDir; Mode=$Mode; VerifiedFiles=$entries.Count; OpenXRRuntime=$runtime }
