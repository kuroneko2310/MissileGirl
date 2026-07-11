// // Copyright (c) 2026 ViralReaction
// //
// // This program and the accompanying materials are made available under the
// // terms of the Eclipse Public License 2.0 which is available at
// // http://www.eclipse.org/legal/epl-2.0.
// //
// // SPDX-License-Identifier: EPL-2.0

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
            Context.Core = LoadedModManager.RunningMods.First(mod => mod.IsCoreMod);

            Directory.CreateDirectory(GagarinEnvironmentInfo.CacheFolderPath);
            Directory.CreateDirectory(GagarinEnvironmentInfo.TexturesFolderPath);
            Directory.CreateDirectory(GagarinEnvironmentInfo.AssetPipelineFolderPath);

            Logger.Message("GAGARIN: <color=green>Loading cache settings and Asset Pipeline Manifest V2</color>");
            GagarinSettings.LoadSettings();

            try
            {
                AssetPipelineCoordinator.Initialize(Context.RunningMods);
            }
            catch (Exception exception)
            {
                Context.IsUsingCache = false;
                Log.Warning($"GAGARIN: Asset Pipeline initialization failed. Vanilla XML processing will be used.\n{exception}");
                Logger.Debug("GAGARIN: Asset Pipeline initialization failure", exception);
            }

            if (GagarinPrefs.CacheExpires && GagarinPrefs.CacheCreationTime != default(DateTime) &&
                DateTime.Now.Subtract(GagarinPrefs.CacheCreationTime).Days >= GagarinPrefs.CacheRetentionTime)
            {
                GagarinPrefs.CacheCreationTime = default(DateTime);
                AssetPipelineCoordinator.RequestXmlRebuild("Cache retention period expired");
                GagarinSettings.WriteSettings();
            }

            Context.IsUsingCache = GagarinPrefs.Enabled &&
                                   AssetPipelineCoordinator.CanUseXmlCache &&
                                   GagarinEnvironmentInfo.CacheExists;

            RunningModsSetUtility.Dump(Context.RunningMods, GagarinEnvironmentInfo.ModListFilePath);

            if (GagarinPrefs.Enabled)
            {
                if (!Context.IsUsingCache)
                    Log.Warning("GAGARIN: No valid READY XML generation is active; rebuilding through RimWorld's normal XML pipeline.");
                GagarinPatcher.PatchAll();
            }
            else
            {
                Log.Message("GAGARIN: <color=red>Missile Girl's XML caching is disabled.</color>");
            }
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

            try
            {
                AssetPipelineCoordinator.PruneOldData();
                AssetPipelineCoordinator.RecordStartupComplete();
            }
            catch (Exception exception)
            {
                Log.Warning($"GAGARIN: Asset Pipeline cleanup failed without affecting startup.\n{exception}");
            }
        }
    }
}
