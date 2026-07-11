// // Copyright (c) 2026 ViralReaction
// //
// // This program and the accompanying materials are made available under the
// // terms of the Eclipse Public License 2.0 which is available at
// // http://www.eclipse.org/legal/epl-2.0.
// //
// // SPDX-License-Identifier: EPL-2.0

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
                catch (Exception er)
                {
                    Context.AssetsHashes.Clear();
                    Context.AssetsHashesInt.Clear();
                    DisableCache("loading asset hash files", er);
                }
            }

            public static void Postfix(IEnumerable<LoadableXmlAsset> __result)
            {
                try
                {
                    Context.XmlAssets = __result?
                        .Where(asset => asset != null && !asset.FullFilePath.NullOrEmpty())
                        .GroupBy(asset => asset.FullFilePath)
                        .ToDictionary(group => group.Key, group => group.Last())
                        ?? new Dictionary<string, LoadableXmlAsset>();

                    if (Context.IsUsingCache && Context.Assets.Count != Context.AssetsHashes.Count)
                    {
                        Context.IsUsingCache = false;
                        Context.AssetsHashes.RemoveAll(asset => !Context.Assets.Contains(asset.Key));
                        Context.AssetsHashesInt.RemoveAll(asset => !Context.Assets.Contains(asset.Key));
                        Log.Warning("GAGARIN: Total number of files changed. Resetting XML cache.");
                    }

                    if (!Context.IsUsingCache)
                    {
                        AssetHashingUtility.Dump(Context.AssetsHashes, GagarinEnvironmentInfo.HashFilePath);
                        AssetHashingUtility.Dump(Context.AssetsHashesInt, GagarinEnvironmentInfo.HashFilePathInt);
                    }
                }
                catch (Exception er)
                {
                    DisableCache("finalizing XML asset discovery", er);
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

                foreach (var mod in Context.RunningMods)
                {
                    if (mod.patches != null)
                    {
                        foreach (var patch in mod.patches)
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
                catch (Exception er)
                {
                    DisableCache("preparing the XML cache", er);
                    return true;
                }
            }

            public static void Postfix(XmlDocument xmlDoc)
            {
                if (Context.IsUsingCache || xmlDoc == null)
                    return;

                try
                {
                    var settings = new XmlWriterSettings
                    {
                        CheckCharacters = false,
                        Indent = true,
                        NewLineChars = "\n"
                    };
                    AtomicFile.SaveXml(GagarinEnvironmentInfo.UnifiedPatchedOriginalXmlPath, xmlDoc, settings);
                }
                catch (Exception er)
                {
                    Logger.Debug("GAGARIN: Failed to write the diagnostic unified XML", er);
                    Log.Warning("GAGARIN: Failed to write diagnostic unified XML. Continuing without it.\n" + er);
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
                    AssetPipelineCoordinator.CommitXmlGeneration();
                    GagarinPrefs.CacheCreationTime = DateTime.Now;
                    GagarinSettings.WriteSettings();
                }
                catch (Exception er)
                {
                    DisableCache("saving the completed XML cache", er);
                }
            }
        }

        [GagarinPatch(typeof(LoadedModManager), nameof(LoadedModManager.CombineIntoUnifiedXML))]
        public static class CombineIntoUnifiedXML_Patch
        {
            [HarmonyPriority(Priority.Last)]
            public static bool Prefix(
                List<LoadableXmlAsset> xmls,
                ref XmlDocument __result,
                Dictionary<XmlNode, LoadableXmlAsset> assetlookup,
                out bool __state)
            {
                __state = false;
                Context.DefsXmlAssets = assetlookup;

                if (Prefs.LogVerbose)
                    Log.Warning("GAGARIN: CombineIntoUnifiedXML has <color=red>Context.IsUsingCache=" + Context.IsUsingCache + "</color>");

                if (/Context.IsUsingCache)
                    return true;

                try
                {
                    var cachedDocument = new XmlDocument();
                    CachedDefHelper.Load(cachedDocument, assetlookup);
                    __result = cachedDocument;
                    __state = true;
                    return false;
                }
                catch (Exception er)
                {
                    __result = null;
                    assetlookup?.Clear();
                    DisableCache("loading Unified.xml", er);
                    Log.Warning("GAGARIN: Falling back to RimWorld's normal XML combination path.");
                    return true;
                }
            }

            [HarmonyPriority(Priority.First)]
            public static void Postfix(
                XmlDocument __result,
                Dictionary<XmlNode, LoadableXmlAsset> assetlookup,
                bool __state)
            {
                if (__state || __result == null || assetlookup.EnumerableNullOrEmpty())
                    return;

                try
                {
                    DuplicateHelper.ParseCreateReports(__result, assetlookup);
                }
                catch (Exception er)
                {
                    Logger.Debug("GAGARIN: Failed to create duplicate XML reports", er);
                    Log.Warning("GAGARIN: Duplicate XML report generation failed. Continuing startup.\n" + er);
                }
            }
        }

        private static void DisableCache(string stage, Exception exception)
        {
            try
            {
                AssetPipelineCoordinator.OnCacheLoadFailure(stage, exception);
            }
            catch (Exception disableException)
            {
                Context.IsUsingCache = false;
                Logger.Debug("GAGARIN: Failed to disable cache while " + stage, disableException);
            }

            Logger.Debug("GAGARIN: Cache error while " + stage, exception);
            Log.Warning("GAGARIN: Cache error while " + stage + ". The cache has been disabled for this load.\n" + exception);
        }


    }
}
