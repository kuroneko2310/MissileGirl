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

            // Settings must be loaded before cache age and game-build validation.
            Logger.Message("GAGARIN: <color=green>Loading cache settings!</color>");
            GagarinSettings.LoadSettings();

            Context.IsUsingCache = GagarinEnvironmentInfo.CacheExists;
            if (Context.IsUsingCache && GagarinEnvironmentInfo.ModListChanged)
            {
                Context.IsUsingCache = false;
                Log.Warning("GAGARIN: Mod order or mod content changed. Rebuilding XML cache.");
            }

            if (Context.IsUsingCache && IsCacheExpired())
            {
                GagarinPrefs.CacheCreationTime = default;
                Context.IsUsingCache = false;
                Log.Warning("GAGARIN: Cache expired.");
                GagarinSettings.WriteSettings();
            }

            // Persist the state that was actually inspected. These files are written atomically.
            RunningModsSetUtility.Dump(Context.RunningMods, GagarinEnvironmentInfo.ModListFilePath);
            ModFingerprintUtility.Dump(Context.RunningMods, GagarinEnvironmentInfo.ModFingerprintFilePath);

            if (!Context.IsUsingCache && GagarinPrefs.Enabled)
                Log.Warning("GAGARIN: <color=green>Cache not found, invalid or scheduled for rebuilding.</color>");

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

            // A clock correction into the future should not make a cache immortal.
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
