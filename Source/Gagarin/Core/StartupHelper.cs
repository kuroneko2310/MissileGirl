using System;
using System.IO;
using System.Linq;
using MissileGirl;
using Verse;

namespace Gagarin
{
    public static class StartupHelper
    {
        [Main.OnInitialization]
        public static void StartUpStarted()
        {
            Context.RunningMods = LoadedModManager.RunningMods.ToList();
            Context.Core = LoadedModManager.RunningMods.FirstOrDefault(mod => mod.IsCoreMod);

            Directory.CreateDirectory(GagarinEnvironmentInfo.CacheFolderPath);
            Directory.CreateDirectory(GagarinEnvironmentInfo.TexturesFolderPath);

            if (Prefs.LogVerbose)
                Log.Message("GAGARIN: <color=green>StartUpStarted called!</color>");

            Logger.Message("GAGARIN: <color=green>Loading cache settings!</color>");
            GagarinSettings.LoadSettings();

            Context.IsUsingCache = GagarinEnvironmentInfo.CacheExists;
            if (Context.IsUsingCache && GagarinEnvironmentInfo.XmlInputsChanged)
            {
                Context.IsUsingCache = false;
                Log.Warning("GAGARIN: XML inputs, assemblies, mod order, or active load folders changed. Rebuilding XML cache.");
            }

            if (GagarinEnvironmentInfo.TextureInputsChanged)
            {
                GagarinCacheManager.ClearTextureCache("mod textures, assemblies, or active load folders changed");
                Log.Message("GAGARIN: Texture inputs changed. XML cache was preserved where possible.");
            }

            if (Context.IsUsingCache && IsCacheExpired())
            {
                GagarinPrefs.CacheCreationTime = default;
                Context.IsUsingCache = false;
                Log.Warning("GAGARIN: XML cache expired.");
                GagarinSettings.WriteSettings();
            }

            RunningModsSetUtility.Dump(Context.RunningMods, GagarinEnvironmentInfo.ModListFilePath);
            ModFingerprintUtility.Dump(Context.RunningMods, GagarinEnvironmentInfo.XmlFingerprintFilePath,
                ModFingerprintDomain.Xml);
            ModFingerprintUtility.Dump(Context.RunningMods, GagarinEnvironmentInfo.TextureFingerprintFilePath,
                ModFingerprintDomain.Textures);

            if (!Context.IsUsingCache && GagarinPrefs.Enabled)
                Log.Warning("GAGARIN: <color=green>XML cache not found, invalid, or scheduled for rebuilding.</color>");

            if (GagarinPrefs.Enabled)
                GagarinPatcher.PatchAll();
            else
                Log.Message("GAGARIN: <color=red>Missile Girl's XML caching is disabled!</color>");
        }

        private static bool IsCacheExpired()
        {
            if (!GagarinPrefs.CacheExpires)
                return false;

            DateTime creationTime = GagarinPrefs.CacheCreationTime;
            if (creationTime == default)
                return true;

            if (creationTime > DateTime.Now.AddMinutes(5))
                return true;

            return DateTime.Now.Subtract(creationTime).TotalDays >= Math.Max(1, GagarinPrefs.CacheRetentionTime);
        }

        [Main.OnStaticConstructor]
        public static void StartUpFinished()
        {
            if (Prefs.LogVerbose)
                Log.Message("GAGARIN: <color=green>StartUpFinished called!</color>");

            Context.Assets.Clear();
            Context.AssetsHashes.Clear();
            Context.AssetsHashesInt.Clear();
            Context.DefsXmlAssets.Clear();
            Context.XmlAssets.Clear();
            Context.CurrentLoadingMod = null;

            CachedDefHelper.Clean();
        }
    }
}
