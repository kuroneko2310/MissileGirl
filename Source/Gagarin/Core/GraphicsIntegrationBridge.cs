using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Verse;

namespace Gagarin
{
    public static class GraphicsIntegrationBridge
    {
        private static readonly object Sync = new object();

        public static string GetTextureCacheFolder()
        {
            if (!Directory.Exists(GagarinEnvironmentInfo.TexturesFolderPath))
                Directory.CreateDirectory(GagarinEnvironmentInfo.TexturesFolderPath);
            return GagarinEnvironmentInfo.TexturesFolderPath;
        }

        public static void RegisterGraphicsProvider(string providerId, string providerVersion,
            string texturePolicyFingerprint)
        {
            if (providerId.NullOrEmpty() || texturePolicyFingerprint.NullOrEmpty())
                return;

            lock (Sync)
            {
                string state = $"{providerId}|{providerVersion ?? "unknown"}|{texturePolicyFingerprint}";
                string statePath = GetProviderStatePath(providerId);
                string previousState = ReadStateSafely(statePath);

                if (string.Equals(previousState, state, StringComparison.Ordinal))
                    return;

                GagarinCacheManager.ClearTextureCache(
                    previousState.NullOrEmpty()
                        ? $"registered or migrated graphics provider {providerId}"
                        : $"graphics policy changed for {providerId}");

                AtomicFile.Write(statePath, temporaryPath =>
                    File.WriteAllText(temporaryPath, state, new UTF8Encoding(false)));
                Log.Message($"GAGARIN: Graphics provider registered: {providerId} ({providerVersion})");
            }
        }

        private static string GetProviderStatePath(string providerId)
        {
            using SHA256 sha = SHA256.Create();
            string providerHash = BitConverter.ToString(
                    sha.ComputeHash(Encoding.UTF8.GetBytes(providerId.ToLowerInvariant())))
                .Replace("-", string.Empty)
                .Substring(0, 16);
            return Path.Combine(GagarinEnvironmentInfo.CacheFolderPath,
                $"GraphicsProvider-{providerHash}.state");
        }

        private static string ReadStateSafely(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
            }
            catch (Exception exception)
            {
                Log.Warning($"GAGARIN: Graphics provider state was unreadable and will be rebuilt: {exception.Message}");
                return null;
            }
        }
    }
}
