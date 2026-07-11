using System;
using System.IO;
using Verse;

namespace Gagarin
{
    public static class GraphicsIntegrationBridge
    {
        private const string ProviderStateFileName = "GraphicsProvider.state";
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
                string statePath = Path.Combine(GetTextureCacheFolder(), ProviderStateFileName);
                string previousState = File.Exists(statePath) ? File.ReadAllText(statePath) : null;

                if (string.Equals(previousState, state, StringComparison.Ordinal))
                    return;

                GagarinCacheManager.ClearTextureCache(
                    previousState.NullOrEmpty()
                        ? $"registered graphics provider {providerId}"
                        : $"graphics policy changed for {providerId}");

                statePath = Path.Combine(GetTextureCacheFolder(), ProviderStateFileName);
                AtomicFile.Write(statePath, temporaryPath => File.WriteAllText(temporaryPath, state));
                Log.Message($"GAGARIN: Graphics provider registered: {providerId} ({providerVersion})");
            }
        }
    }
}
