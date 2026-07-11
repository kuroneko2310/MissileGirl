using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using HarmonyLib;
using MissileGirl;
using Verse;

namespace Gagarin
{
    public static class LoadedModManager_Patch
    {
        [GagarinPatch(typeof(LoadedModManager), nameof(LoadedModManager.LoadModXML))]
        public static class LoadModXML_Patch
        {
            public static void Prefix()
            {
                Context.IsLoadingModXML = true;
                try
                {
                    if (File.Exists(GagarinEnvironmentInfo.HashFilePath))
                        Context.AssetsHashes = AssetHashingUtility.Load(GagarinEnvironmentInfo.HashFilePath);
                    if (File.Exists(GagarinEnvironmentInfo.HashFilePathInt))
                        Context.AssetsHashesInt = AssetHashingUtility.LoadInt(GagarinEnvironmentInfo.HashFilePathInt);
                }
                catch (Exception exception)
                {
                    Context.IsUsingCache = false;
                    Logger.Debug("GAGARIN: Failed loading hash manifests; rebuilding cache", exception);
                }
            }

            public static void Postfix(IEnumerable<LoadableXmlAsset> __result)
            {
                try
                {
                    Context.XmlAssets = new Dictionary<string, LoadableXmlAsset>();
                    if (__result != null)
                    {
                        foreach (LoadableXmlAsset asset in __result)
                            Context.XmlAssets[asset.FullFilePath] = asset;
                    }

                    if (Context.IsUsingCache && Context.Assets.Count != Context.AssetsHashes.Count)
                    {
                        Context.IsUsingCache = false;
                        Context.AssetsHashes.RemoveAll(pair => !Context.Assets.Contains(pair.Key));
                        Context.AssetsHashesInt.RemoveAll(pair => !Context.Assets.Contains(pair.Key));
                        Log.Warning("GAGARIN: Total number of XML files changed. Rebuilding cache.");
                    }

                    if (!Context.IsUsingCache)
                    {
                        AssetHashingUtility.Dump(Context.AssetsHashes, GagarinEnvironmentInfo.HashFilePath);
                        AssetHashingUtility.Dump(Context.AssetsHashesInt, GagarinEnvironmentInfo.HashFilePathInt);
                        if (File.Exists(GagarinEnvironmentInfo.UnifiedXmlFilePath))
                            File.Delete(GagarinEnvironmentInfo.UnifiedXmlFilePath);
                    }
                }
                catch (Exception exception)
                {
                    Context.IsUsingCache = false;
                    Logger.Debug("GAGARIN: Error finalizing XML asset scan; continuing without cache", exception);
                }
                finally
                {
                    Context.IsLoadingModXML = false;
                }
            }
        }

        [GagarinPatch(typeof(TKeySystem), nameof(TKeySystem.Parse))]
        public static class TKeySystem_Parse_Patch
        {
            public static void Postfix()
            {
                DuplicateHelper.QueueReportProcessing();
            }
        }

        [GagarinPatch(typeof(LoadedModManager), nameof(LoadedModManager.ClearCachedPatches))]
        public static class ClearCachedPatches_Patch
        {
            public static bool Prefix()
            {
                if (!Context.IsUsingCache)
                    return true;

                foreach (ModContentPack mod in Context.RunningMods)
                {
                    if (mod.patches != null)
                    {
                        foreach (PatchOperation patch in mod.patches)
                            patch.neverSucceeded = false;
                    }
                    mod.loadedAnyPatches = true;
                }
                return false;
            }
        }

        [GagarinPatch(typeof(LoadedModManager), nameof(LoadedModManager.ApplyPatches))]
        public static class ApplyPatches_Patch
        {
            [HarmonyPriority(Priority.Last)]
            public static bool Prefix()
            {
                try
                {
                    CachedDefHelper.Prepare();
                    return !Context.IsUsingCache;
                }
                catch (Exception exception)
                {
                    Context.IsUsingCache = false;
                    Logger.Debug("GAGARIN: Cache preparation failed; applying patches normally", exception);
                    CachedDefHelper.Prepare();
                    return true;
                }
            }

            public static void Postfix(XmlDocument xmlDoc)
            {
                if (Context.IsUsingCache || xmlDoc == null)
                    return;

                try
                {
                    AtomicFile.Write(GagarinEnvironmentInfo.UnifiedPatchedOriginalXmlPath, temporaryPath =>
                    {
                        XmlWriterSettings settings = new XmlWriterSettings
                        {
                            CheckCharacters = false,
                            Indent = true,
                            NewLineChars = "\n"
                        };
                        using XmlWriter writer = XmlWriter.Create(temporaryPath, settings);
                        xmlDoc.Save(writer);
                    });
                }
                catch (Exception exception)
                {
                    Logger.Debug("GAGARIN: Failed writing diagnostic patched XML", exception);
                }
            }
        }

        [GagarinPatch(typeof(LoadedModManager), nameof(LoadedModManager.ParseAndProcessXML))]
        public static class ParseAndProcessXML_Patch
        {
            [HarmonyPriority(Priority.Last)]
            public static void Postfix()
            {
                if (Context.IsUsingCache)
                    return;

                try
                {
                    CachedDefHelper.Save();
                    GagarinPrefs.CacheCreationTime = DateTime.Now;
                    RunningModsSetUtility.Dump(Context.RunningMods, GagarinEnvironmentInfo.ModListFilePath);
                    ModFingerprintUtility.Dump(Context.RunningMods, GagarinEnvironmentInfo.XmlFingerprintFilePath,
                        ModFingerprintDomain.Xml);
                    GagarinSettings.WriteSettings();
                }
                catch (Exception exception)
                {
                    GagarinPrefs.CacheCreationTime = default;
                    GagarinCacheManager.InvalidateXmlCache("cache build failed");
                    Logger.Debug("GAGARIN: Failed creating XML cache; game will continue without it", exception);
                }
            }
        }

        [GagarinPatch(typeof(LoadedModManager), nameof(LoadedModManager.CombineIntoUnifiedXML))]
        public static class CombineIntoUnifiedXML_Patch
        {
            [HarmonyPriority(Priority.Last)]
            public static bool Prefix(List<LoadableXmlAsset> xmls, ref XmlDocument __result,
                Dictionary<XmlNode, LoadableXmlAsset> assetlookup, ref bool __state)
            {
                __state = false;
                Context.DefsXmlAssets = assetlookup;

                if (!Context.IsUsingCache)
                    return true;

                try
                {
                    if (Prefs.LogVerbose)
                        Log.Warning("GAGARIN: Attempting to load unified XML cache");

                    CachedDefHelper.Load(__result = new XmlDocument(), assetlookup);
                    __state = true;
                    return false;
                }
                catch (Exception exception)
                {
                    Context.IsUsingCache = false;
                    __result = null;
                    Logger.Debug("GAGARIN: Unified XML cache was invalid; falling back to normal XML loading", exception);
                    Log.Warning("GAGARIN: Unified XML cache could not be loaded. Rebuilding it during this startup.");
                    return true;
                }
            }

            [HarmonyPriority(Priority.First)]
            public static void Postfix(XmlDocument __result,
                Dictionary<XmlNode, LoadableXmlAsset> assetlookup, bool __state)
            {
                if (__state || __result == null || assetlookup.EnumerableNullOrEmpty())
                    return;

                try
                {
                    DuplicateHelper.ParseCreateReports(__result, assetlookup);
                }
                catch (Exception exception)
                {
                    Logger.Debug("GAGARIN: Duplicate report generation failed", exception);
                }
            }
        }
    }
}
