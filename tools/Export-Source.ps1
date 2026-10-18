[CmdletBinding()]
param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$Destination = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $Destination) { throw 'Choose a new archive path.' }
$parent = Split-Path $Destination -Parent
if (!(Test-Path -LiteralPath $parent -PathType Container)) { throw 'Archive parent folder must exist.' }
$archive = [IO.Compression.ZipFile]::Open($Destination, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $repo -Recurse -Force -File) {
        $relative = [IO.Path]::GetRelativePath($repo, $file.FullName).Replace('\','/')
        $parts = $relative.Split('/')
        if ($parts | Where-Object { $_ -in @('bin','obj','.git','work','.vs') }) { continue }
        if ($file.Name -eq 'Local.Build.props' -or $file.Extension -in @('.dll','.exe','.pdb','.zip','.log','.tgz','.suo','.user')) { continue }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, ('ScamWithYourFriendsInVR/' + $relative), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
Write-Output ('Source archive: ' + $Destination)
