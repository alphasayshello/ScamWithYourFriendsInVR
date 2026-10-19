[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameDir)
$ErrorActionPreference = 'Stop'
$player = Join-Path $GameDir 'UnityPlayer.dll'
if (!(Test-Path -LiteralPath $player -PathType Leaf)) { throw 'GameDir does not contain UnityPlayer.dll.' }
$version = (Get-Item -LiteralPath $player).VersionInfo.ProductVersion
$matchedProfiles = @()
$supported = @()
foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'dependencies*.lock.json' -File) {
    $dependencyProfile = Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json
    $supported += $dependencyProfile.unityVersion
    if ($version -match ('^' + [regex]::Escape($dependencyProfile.unityVersion) + '([_\s]|$)')) { $matchedProfiles += $dependencyProfile }
}
if ($matchedProfiles.Count -eq 0) { throw "Unsupported Unity game build $version. Supported targets: $($supported -join ', '). Use a matching game copy and editor." }
if ($matchedProfiles.Count -ne 1) { throw 'Multiple dependency profiles match this game. Remove duplicate lock files.' }
$matchedProfiles[0]
