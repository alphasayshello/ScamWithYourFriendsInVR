using System;
using System.IO;
using System.Threading;

namespace ScamWithYourFriendsInVR.Build
{
    public static class AtomicOutput
    {
        public static void Write(string destination, Action<string> writer)
        {
            destination = Path.GetFullPath(destination);
            string parent = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(parent);
            string temporary = Path.Combine(parent, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                writer(temporary);
                for (int attempt = 0; ; attempt++)
                {
                    try { File.Move(temporary, destination, true); break; }
                    catch (IOException ex) when (attempt < 19 && ((ex.HResult & 0xffff) == 32 || (ex.HResult & 0xffff) == 33))
                    { Thread.Sleep(50); }
                    // Windows also reports a destination opened without delete sharing as access denied.
                    catch (UnauthorizedAccessException ex) when (attempt < 19 && (ex.HResult & 0xffff) == 5)
                    { Thread.Sleep(50); }
                }
            }
            finally
            {
                if (File.Exists(temporary))
                    try { File.Delete(temporary); }
                    catch (IOException) { Console.Error.WriteLine("Temporary output remains in the incomplete stage: " + temporary); }
            }
        }
    }
}
