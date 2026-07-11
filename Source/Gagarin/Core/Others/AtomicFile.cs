using System;
using System.IO;

namespace Gagarin
{
    internal static class AtomicFile
    {
        public static void Write(string path, Action<string> writeTemporaryFile)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string temporaryPath = path + ".tmp";
            string backupPath = path + ".bak";

            TryDelete(temporaryPath);
            writeTemporaryFile(temporaryPath);

            try
            {
                if (File.Exists(path))
                {
                    TryDelete(backupPath);
                    File.Replace(temporaryPath, path, backupPath, true);
                    TryDelete(backupPath);
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            catch (PlatformNotSupportedException)
            {
                ReplaceWithFallback(path, temporaryPath, backupPath);
            }
            catch (IOException)
            {
                ReplaceWithFallback(path, temporaryPath, backupPath);
            }
        }

        public static bool TryRestoreBackup(string path)
        {
            string backupPath = path + ".bak";
            if (!File.Exists(backupPath))
                return false;

            try
            {
                TryDelete(path);
                File.Move(backupPath, path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ReplaceWithFallback(string path, string temporaryPath, string backupPath)
        {
            if (File.Exists(path))
            {
                TryDelete(backupPath);
                File.Move(path, backupPath);
            }

            try
            {
                File.Move(temporaryPath, path);
                TryDelete(backupPath);
            }
            catch
            {
                if (!File.Exists(path) && File.Exists(backupPath))
                    File.Move(backupPath, path);
                throw;
            }
        }

        private static void TryDelete(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
