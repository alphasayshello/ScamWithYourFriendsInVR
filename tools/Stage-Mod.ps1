[CmdletBinding()]
param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$propsFile = Join-Path $repo 'Local.Build.props'
if (!(Test-Path -LiteralPath $propsFile)) { throw 'Run Prepare-Dependencies.ps1 first.' }
[xml]$props = Get-Content -Raw -LiteralPath $propsFile
$values = @{}
foreach ($node in $props.Project.PropertyGroup.ChildNodes) { $values[$node.Name] = $node.InnerText }
$lock = & (Join-Path $PSScriptRoot 'Resolve-Dependencies.ps1') -GameDir $values.GameDir
$playerPath = Join-Path $values.GameDir 'UnityPlayer.dll'
if (!(Test-Path -LiteralPath $playerPath)) { throw 'Configured GameDir no longer contains UnityPlayer.dll. Run dependency preparation again.' }
$gameVersion = (Get-Item -LiteralPath $playerPath).VersionInfo.ProductVersion
if ($gameVersion -notmatch ('^' + [regex]::Escape($lock.unityVersion) + '([_\s]|$)')) {
    throw "Configured game updated: expected Unity $($lock.unityVersion), found $gameVersion. Prepare against a compatible game copy before staging."
}
if (Test-Path -LiteralPath (Join-Path $values.GameDir 'GameAssembly.dll')) { throw 'IL2CPP staging is unsupported.' }
& (Join-Path $PSScriptRoot 'Assert-InputVersion.ps1') -GameDir $values.GameDir -ExpectedVersion ($lock.packages | Where-Object name -eq 'com.unity.inputsystem').version
$Destination = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $Destination) { throw 'Use a new, empty staging destination.' }
$build = Join-Path $repo 'bin\Release\netstandard2.1'
$provider = Join-Path $PSScriptRoot 'provider\bin\Release\netstandard2.1'
foreach ($path in @((Join-Path $build 'ScamWithYourFriendsInVR.Runtime.dll'),(Join-Path $provider 'Unity.XR.OpenXR.dll'))) {
    if (!(Test-Path -LiteralPath $path)) { throw 'Build the mod and provider in Release first.' }
}
New-Item -ItemType Directory -Path $Destination | Out-Null
$entries = @{}
function Stage([string]$Source, [string]$Relative) {
    $target = [IO.Path]::GetFullPath((Join-Path $Destination $Relative))
    if (!$target.StartsWith($Destination + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Path escaped staging root.' }
    New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
    Copy-Item -LiteralPath $Source -Destination $target -Force
    $entries[$Relative] = [pscustomobject]@{path=$Relative; sha256=(Get-FileHash -LiteralPath $target).Hash}
}
$bepRoot = Split-Path $values.BepInExDir -Parent
foreach ($file in Get-ChildItem -LiteralPath $bepRoot -Recurse -File) {
    if ($file.Extension -eq '.pdb' -or $file.Name -eq 'changelog.txt') { continue }
    Stage $file.FullName ([IO.Path]::GetRelativePath($bepRoot, $file.FullName))
}
$data = 'Scam With Your Friends_Data'
$managed = Join-Path $data 'Managed'
foreach ($file in Get-ChildItem -LiteralPath $values.UnityManagedDir -Filter 'UnityEngine*.dll') { Stage $file.FullName (Join-Path $managed $file.Name) }
foreach ($file in Get-ChildItem -LiteralPath $values.UnityBclDir -Filter '*.dll') { Stage $file.FullName (Join-Path $managed $file.Name) }
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $values.UnityBclDir 'Facades') -Filter '*.dll') { Stage $file.FullName (Join-Path $managed $file.Name) }
$compatProject = Join-Path $PSScriptRoot 'engine-compat\EngineCompat.csproj'
& dotnet build $compatProject -c Release ('-p:CecilPath=' + (Join-Path $values.BepInExDir 'core\Mono.Cecil.dll'))
if ($LASTEXITCODE -ne 0) { throw 'Engine compatibility tool build failed.' }
$compat = Join-Path $PSScriptRoot 'engine-compat\bin\Release\net10.0\EngineCompat.dll'
$stockManaged = Join-Path $values.GameDir ($data + '\Managed')
& dotnet $compat --validate-stock $stockManaged
if ($LASTEXITCODE -ne 0) { throw 'GameDir must contain unmodified, matching game libraries; discard this incomplete stage.' }
& dotnet $compat --tree $stockManaged $values.UnityBclDir (Join-Path $Destination $managed)
if ($LASTEXITCODE -ne 0) { throw 'Base library contract restoration failed; discard this incomplete stage.' }
& dotnet $compat --tree $stockManaged $values.UnityManagedDir (Join-Path $Destination $managed)
if ($LASTEXITCODE -ne 0) { throw 'Engine contract restoration failed; discard this incomplete stage.' }
Stage (Join-Path $provider 'Unity.InputSystem.dll') (Join-Path $managed 'Unity.InputSystem.dll')
& (Join-Path $PSScriptRoot 'Assert-InputVersion.ps1') -GameDir $Destination -ExpectedVersion ($lock.packages | Where-Object name -eq 'com.unity.inputsystem').version
$plugin = 'BepInEx\plugins\ScamWithYourFriendsInVR'
foreach ($name in @('ScamWithYourFriendsInVR.dll','ScamWithYourFriendsInVR.Runtime.dll','ScamWithYourFriendsInVR.Multiplayer.dll')) { Stage (Join-Path $build $name) (Join-Path $plugin $name) }
foreach ($name in @('Unity.XR.Management.dll','Unity.XR.OpenXR.dll')) { Stage (Join-Path $provider $name) (Join-Path $plugin $name) }
$openxr = Join-Path $values.XRPackageRoot 'com.unity.xr.openxr\package\Runtime'
Stage (Join-Path $openxr 'UnitySubsystemsManifest.json') (Join-Path $data 'UnitySubsystems\UnityOpenXR\UnitySubsystemsManifest.json')
Stage (Join-Path $openxr 'windows\x64\UnityOpenXR.dll') (Join-Path $data 'Plugins\x86_64\UnityOpenXR.dll')
Stage (Join-Path (Split-Path $openxr -Parent) 'RuntimeLoaders\windows\x64\openxr_loader.dll') (Join-Path $data 'Plugins\x86_64\openxr_loader.dll')
Stage (Join-Path (Split-Path $openxr -Parent) 'RuntimeLoaders\windows\x64\openxr_loader.dll') 'openxr_loader.dll'
[ordered]@{
    schemaVersion = 1
    unityVersion = $lock.unityVersion
    inputSystemVersion = ($lock.packages | Where-Object name -eq 'com.unity.inputsystem').version
    unityPlayerSha256 = (Get-FileHash -LiteralPath (Join-Path $values.GameDir 'UnityPlayer.dll') -Algorithm SHA256).Hash
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Destination 'swyf-vr-build.json')
$entries.Clear()
foreach ($file in Get-ChildItem -LiteralPath $Destination -Recurse -File) {
    $relative = [IO.Path]::GetRelativePath($Destination, $file.FullName)
    $entries[$relative] = [pscustomobject]@{path=$relative; sha256=(Get-FileHash -LiteralPath $file.FullName).Hash}
}
$entries.Values | Sort-Object path | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Destination 'swyf-vr-manifest.json')
Write-Output "Staged $($entries.Count) files to $Destination. This folder contains locally supplied Unity runtime binaries; review their licensing before redistributing."
