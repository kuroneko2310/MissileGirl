using System;
using System.IO;
using Verse;

namespace Gagarin
{
    public static class GagarinCacheManager
    {
        public static void InvalidateXmlCache(string reason = null)
        {
            DeleteFile(GagarinEnvironmentInfo.UnifiedXmlFilePath);
            DeleteFile(GagarinEnvironmentInfo.UnifiedPatchedOriginalXmlPath);
            DeleteFile(GagarinEnvironmentInfo.ModListFilePath);
            DeleteFile(GagarinEnvironmentInfo.ModFingerprintFilePath);

            if (!reason.NullOrEmpty())
                Log.Warning($"GAGARIN: XML cache invalidated: {reason}");
        }

        public static void ClearTextureCache(string reason = null)
        {
            try
            {
                if (Directory.Exists(GagarinEnvironmentInfo.TexturesFolderPath))
                    Directory.Delete(GagarinEnvironmentInfo.TexturesFolderPath, true);
                Directory.CreateDirectory(GagarinEnvironmentInfo.TexturesFolderPath);

                if (!reason.NullOrEmpty())
                    Log.Message($"GAGARIN: Texture cache cleared: {reason}");
            }
            catch (Exception exception)
            {
                Log.Error($"GAGARIN: Failed clearing texture cache: {exception}");
                MissileGirl.Logger.Debug("Failed clearing texture cache", exception: exception);
            }
        }

        public static void ClearAllCaches(string reason = null)
        {
            InvalidateXmlCache(reason);
            DeleteFile(GagarinEnvironmentInfo.HashFilePath);
            DeleteFile(GagarinEnvironmentInfo.HashFilePathInt);
            ClearTextureCache(reason);
            GagarinPrefs.CacheCreationTime = default;
        }

        private static void DeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                if (File.Exists(path + ".tmp"))
                    File.Delete(path + ".tmp");
            }
            catch (Exception exception)
            {
                Log.Error($"GAGARIN: Failed deleting cache file '{path}': {exception}");
            }
        }
    }
}
