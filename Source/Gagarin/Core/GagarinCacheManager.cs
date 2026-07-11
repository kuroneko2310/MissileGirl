using System;
using System.IO;
using Verse;

namespace Gagarin
{
    public static class GagarinCacheManager
    {
        private static readonly object CacheMutationSync = new object();

        public static void InvalidateXmlCache(string reason = null)
        {
            lock (CacheMutationSync)
            {
                DeleteFile(GagarinEnvironmentInfo.UnifiedXmlFilePath);
                DeleteFile(GagarinEnvironmentInfo.UnifiedXmlHashFilePath);
                DeleteFile(GagarinEnvironmentInfo.UnifiedPatchedOriginalXmlPath);
                DeleteFile(GagarinEnvironmentInfo.ModListFilePath);
                DeleteFile(GagarinEnvironmentInfo.XmlFingerprintFilePath);
            }

            if (!reason.NullOrEmpty())
                Log.Warning($"GAGARIN: XML cache invalidated: {reason}");
        }

        public static void ClearTextureCache(string reason = null)
        {
            string stalePath = null;
            lock (CacheMutationSync)
            {
                try
                {
                    string texturePath = GagarinEnvironmentInfo.TexturesFolderPath;
                    if (Directory.Exists(texturePath))
                    {
                        stalePath = texturePath + ".stale-" + Guid.NewGuid().ToString("N");
                        try
                        {
                            Directory.Move(texturePath, stalePath);
                        }
                        catch (IOException)
                        {
                            Directory.Delete(texturePath, true);
                            stalePath = null;
                        }
                    }

                    Directory.CreateDirectory(texturePath);
                }
                catch (Exception exception)
                {
                    Log.Error($"GAGARIN: Failed rotating texture cache: {exception}");
                    MissileGirl.Logger.Debug("Failed rotating texture cache", exception: exception);
                    return;
                }
            }

            if (!stalePath.NullOrEmpty())
            {
                try
                {
                    Directory.Delete(stalePath, true);
                }
                catch (Exception exception)
                {
                    Log.Warning($"GAGARIN: Old texture cache was detached but could not yet be deleted: {exception.Message}");
                }
            }

            if (!reason.NullOrEmpty())
                Log.Message($"GAGARIN: Texture cache cleared: {reason}");
        }

        public static void ClearAllCaches(string reason = null)
        {
            InvalidateXmlCache(reason);
            lock (CacheMutationSync)
            {
                DeleteFile(GagarinEnvironmentInfo.HashFilePath);
                DeleteFile(GagarinEnvironmentInfo.HashFilePathInt);
                DeleteFile(GagarinEnvironmentInfo.TextureFingerprintFilePath);
            }
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
                if (File.Exists(path + ".bak"))
                    File.Delete(path + ".bak");
            }
            catch (Exception exception)
            {
                Log.Error($"GAGARIN: Failed deleting cache file '{path}': {exception}");
            }
        }
    }
}
