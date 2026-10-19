[CmdletBinding()]
param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Scam With Your Friends',
    [Parameter(Mandatory)][string]$UnityEditorDir,
    [string]$CacheDir
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$lock = & (Join-Path $PSScriptRoot 'Resolve-Dependencies.ps1') -GameDir $GameDir
if (!$CacheDir) { $CacheDir = Join-Path $repo ('work\dependencies\' + $lock.unityVersion) }
$CacheDir = [IO.Path]::GetFullPath($CacheDir)
$editorData = Join-Path $UnityEditorDir 'Editor\Data'
$player = Join-Path $GameDir 'UnityPlayer.dll'
if (!(Test-Path -LiteralPath $player)) { throw 'GameDir does not contain UnityPlayer.dll.' }
$gameVersion = (Get-Item -LiteralPath $player).VersionInfo.ProductVersion
if ($gameVersion -notmatch ('^' + [regex]::Escape($lock.unityVersion) + '([_\s]|$)')) { throw "Expected Unity $($lock.unityVersion); found $gameVersion" }
& (Join-Path $PSScriptRoot 'Assert-InputVersion.ps1') -GameDir $GameDir -ExpectedVersion ($lock.packages | Where-Object name -eq 'com.unity.inputsystem').version
$unityExe = Join-Path $UnityEditorDir 'Editor\Unity.exe'
if (!(Test-Path -LiteralPath $unityExe)) { throw 'UnityEditorDir must contain Editor\Unity.exe.' }
$editorVersion = (Get-Item -LiteralPath $unityExe).VersionInfo.ProductVersion
if ($editorVersion -notmatch ('^' + [regex]::Escape($lock.unityVersion) + '([_\s]|$)')) { throw "Matching editor required; found $editorVersion" }
$managed = Join-Path $editorData 'PlaybackEngines\windowsstandalonesupport\Variations\win64_player_nondevelopment_mono\Data\Managed'
$bcl = Join-Path $editorData 'MonoBleedingEdge\lib\mono\unityjit-win32'
if (!(Test-Path (Join-Path $managed 'UnityEngine.XRModule.dll')) -or !(Test-Path (Join-Path $bcl 'mscorlib.dll'))) { throw 'Editor lacks Windows Mono player libraries.' }
New-Item -ItemType Directory -Force $CacheDir | Out-Null
function Get-VerifiedFile([string]$Url, [string]$Path, [string]$Hash) {
    if (!(Test-Path -LiteralPath $Path)) { Invoke-WebRequest -Uri $Url -OutFile $Path }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Hash) { throw "Hash mismatch: $Path. Remove the corrupt cached file and retry." }
}
$bepArchive = Join-Path $CacheDir 'bepinex.zip'
Get-VerifiedFile $lock.bepinex.url $bepArchive $lock.bepinex.sha256
$bep = Join-Path $CacheDir 'bepinex'
Expand-Archive -LiteralPath $bepArchive -DestinationPath $bep -Force
$packages = Join-Path $CacheDir 'packages'
New-Item -ItemType Directory -Force $packages | Out-Null
foreach ($package in $lock.packages) {
    $archive = Join-Path $packages ($package.name + '.tgz')
    $url = 'https://download.packages.unity.com/' + $package.name + '/-/' + $package.name + '-' + $package.version + '.tgz'
    Get-VerifiedFile $url $archive $package.sha256
    $destination = Join-Path $packages $package.name
    New-Item -ItemType Directory -Force $destination | Out-Null
    $entries = & tar -tf $archive
    if ($LASTEXITCODE -ne 0) { throw "Cannot read $archive" }
    foreach ($entry in $entries) {
        if ($entry -match '(^[/\\]|^[A-Za-z]:|(^|[/\\])\.\.([/\\]|$))') { throw 'Unsafe archive entry.' }
    }
    & tar -xf $archive -C $destination
    if ($LASTEXITCODE -ne 0) { throw "Cannot extract $archive" }
}
$values = [ordered]@{ GameDir=[IO.Path]::GetFullPath($GameDir); BepInExDir=(Join-Path $bep 'BepInEx'); UnityManagedDir=$managed; UnityBclDir=$bcl; XRPackageRoot=$packages }
$xml = New-Object System.Xml.XmlDocument
$project = $xml.CreateElement('Project'); [void]$xml.AppendChild($project)
$group = $xml.CreateElement('PropertyGroup'); [void]$project.AppendChild($group)
foreach ($name in $values.Keys) { $element=$xml.CreateElement($name); $element.InnerText=$values[$name]; [void]$group.AppendChild($element) }
$xml.Save((Join-Path $repo 'Local.Build.props'))
Write-Output 'Dependencies verified. Run dotnet build -c Release from the repository.'
