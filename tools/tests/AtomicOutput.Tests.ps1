[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if (!$IsWindows) { throw 'These sharing-lock checks require Windows and PowerShell 7.' }
$tools = Split-Path $PSScriptRoot -Parent
$source = [IO.File]::ReadAllText((Join-Path $tools 'engine-compat\AtomicOutput.cs'))
$probe = @'
namespace ScamWithYourFriendsInVR.Build
{
    public static class AtomicOutputProbe
    {
        public static void Verify(string path)
        {
            System.IO.File.WriteAllText(path, "original");
            var held = System.IO.File.Open(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read);
            var update = System.Threading.Tasks.Task.Run(() => AtomicOutput.Write(path, temp => System.IO.File.WriteAllText(temp, "complete")));
            try
            {
                System.Threading.Thread.Sleep(150);
                if (update.IsFaulted) throw new System.Exception("Atomic update failed while the sharing lock was held.", update.Exception);
                if (update.IsCompleted || System.IO.File.ReadAllText(path) != "original")
                    throw new System.Exception("Locked output changed before replacement could succeed.");
            }
            finally { held.Dispose(); }
            update.GetAwaiter().GetResult();
            if (System.IO.File.ReadAllText(path) != "complete") throw new System.Exception("Replacement did not complete after lock release.");
            bool failed = false;
            try { AtomicOutput.Write(path, temp => { System.IO.File.WriteAllText(temp, "partial"); throw new System.InvalidOperationException("fixture"); }); }
            catch (System.InvalidOperationException) { failed = true; }
            if (!failed || System.IO.File.ReadAllText(path) != "complete") throw new System.Exception("Failed writer damaged its previous output.");
            failed = false;
            using (System.IO.File.Open(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
            {
                try { AtomicOutput.Write(path, temp => System.IO.File.WriteAllText(temp, "blocked")); }
                catch (System.IO.IOException) { failed = true; }
                catch (System.UnauthorizedAccessException) { failed = true; }
            }
            if (!failed || System.IO.File.ReadAllText(path) != "complete") throw new System.Exception("Permanent lock did not fail safely after bounded retries.");
            if (System.IO.Directory.GetFiles(System.IO.Path.GetDirectoryName(path)).Length != 1)
                throw new System.Exception("Atomic output left temporary files after fixture cleanup.");
        }
    }
}
'@
if (!('ScamWithYourFriendsInVR.Build.AtomicOutputProbe' -as [type])) { Add-Type -TypeDefinition ($source + "`r`n" + $probe) }
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$fixture = Join-Path $tempRoot ('swyf-atomic-check-' + [guid]::NewGuid().ToString('N'))
try {
    [void][IO.Directory]::CreateDirectory($fixture)
    [ScamWithYourFriendsInVR.Build.AtomicOutputProbe]::Verify((Join-Path $fixture 'module.dll'))
    Write-Output 'Atomic output checks passed: transient sharing locks retry, successful replacement is complete, writer failure preserves old data, permanent locks fail safely and temporary outputs are cleaned.'
} finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (!$resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notlike 'swyf-atomic-check-*') { throw 'Refusing cleanup outside the temporary test directory.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
