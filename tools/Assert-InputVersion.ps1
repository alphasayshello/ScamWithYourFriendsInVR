[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDir,
    [Parameter(Mandatory)][string]$ExpectedVersion
)
$ErrorActionPreference = 'Stop'
$path = Join-Path $GameDir 'Scam With Your Friends_Data\Managed\Unity.InputSystem.dll'
if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw 'The game Input System assembly is missing.' }
$expected = [Version]($ExpectedVersion.Split('-')[0] + '.0')
$actual = [Reflection.AssemblyName]::GetAssemblyName($path).Version
if ($actual -ne $expected) {
    throw "Input System version mismatch: expected $expected, found $actual. Use dependencies matching this game build; mismatched input libraries can crash startup asset loading."
}
