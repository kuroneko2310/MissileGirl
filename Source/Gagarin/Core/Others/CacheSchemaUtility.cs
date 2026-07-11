using System;
using System.IO;
using System.Text;

namespace Gagarin
{
    internal static class CacheSchemaUtility
    {
        public const int CurrentVersion = 2;

        public static bool IsCurrent(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return false;

                string value = File.ReadAllText(path, Encoding.UTF8).Trim();
                return int.TryParse(value, out int version) && version == CurrentVersion;
            }
            catch
            {
                return false;
            }
        }

        public static void MarkCurrent(string path)
        {
            AtomicFile.Write(path, temporaryPath =>
                File.WriteAllText(temporaryPath, CurrentVersion.ToString(), new UTF8Encoding(false)));
        }
    }
}
