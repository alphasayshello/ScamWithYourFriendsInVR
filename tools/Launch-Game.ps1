[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDir,
    [ValidateSet('VR','PC')][string]$Mode = 'VR'
)
$ErrorActionPreference = 'Stop'
$setup = & (Join-Path $PSScriptRoot 'Check-Setup.ps1') -GameDir $GameDir -Mode $Mode
if (Get-CimInstance Win32_Process -Filter "Name='Scam With Your Friends.exe'") { throw 'Close the running game before launching another client.' }
$testPlugin = Join-Path $setup.GameDir 'BepInEx\plugins\ScamWithYourFriendsInVR\ScamWithYourFriendsInVR.SmokeTests.dll'
if (Test-Path -LiteralPath $testPlugin) { throw 'Remove the optional smoke-test plugin before normal play.' }
if ($env:DOORSTOP_DISABLE -eq '1' -or $env:DOORSTOP_ENABLED -eq '0') { throw 'Doorstop is disabled in this shell. Clear the override before starting the mod.' }
$configPath = Join-Path $setup.GameDir 'BepInEx\config\com.swyf.vr.cfg'
$config = if (Test-Path -LiteralPath $configPath) { [IO.File]::ReadAllText($configPath) } else { '' }
$section = [regex]::Match($config, '(?ms)^\[General\][ \t]*\r?\n(?<body>.*?)(?=^\[|\z)')
if ($section.Success) {
    $body = $section.Groups['body'].Value
    $body = if ($body -match '(?m)^Mode\s*=') { [regex]::Replace($body, '(?m)^Mode\s*=[^\r\n]*', ('Mode = ' + $Mode)) } else { $body + "`r`nMode = $Mode`r`n" }
    $config = $config.Remove($section.Groups['body'].Index, $section.Groups['body'].Length).Insert($section.Groups['body'].Index, $body)
} else { $config += "`r`n[General]`r`nMode = $Mode`r`n" }
[void][IO.Directory]::CreateDirectory((Split-Path $configPath -Parent))
if (Test-Path -LiteralPath $configPath) { Copy-Item -LiteralPath $configPath -Destination ($configPath + '.before-launch.bak') -Force }
[IO.File]::WriteAllText($configPath, $config)
$process = Start-Process -FilePath (Join-Path $setup.GameDir 'Scam With Your Friends.exe') -WorkingDirectory $setup.GameDir -ArgumentList '-force-d3d11' -PassThru
try {
    if ($process.WaitForExit(2000)) {
        throw "Game closed during startup (exit code $($process.ExitCode)). Keep Steam running and signed in, resolve any game or Steam error, then retry."
    }
    Write-Output "Game process is running in $Mode mode (process $($process.Id))."
    if ($Mode -eq 'VR') { Write-Output 'OpenXR setup passed. Headset connection and in-game tracking still require checking in your headset.' }
} finally { $process.Dispose() }
