// // Copyright (c) 2026 ViralReaction
// //
// // This program and the accompanying materials are made available under the
// // terms of the Eclipse Public License 2.0 which is available at
// // http://www.eclipse.org/legal/epl-2.0.
// //
// // SPDX-License-Identifier: EPL-2.0

using System;
using System.Globalization;
using System.IO;
using System.Xml;
using MissileGirl;
using RimWorld;
using Verse;

namespace Gagarin
{
    public class GagarinSettings : IExposable
    {
        private const string FMT = "yyyy-MM-dd HH:mm:ss.fffffff";

        private string creationDateInt;
        private string gameBuild;

        public void ExposeData()
        {
            if (Prefs.LogVerbose)
                Log.Message("b." + VersionControl.CurrentBuild + ":" + VersionControl.CurrentBuildDate + ":" + VersionControl.CurrentVersionStringWithRev);

            if (Scribe.mode == LoadSaveMode.Saving)
                gameBuild = VersionControl.CurrentBuild + ":" + VersionControl.CurrentBuildDate + ":" + VersionControl.CurrentVersionStringWithRev;

            Scribe_Values.Look(ref gameBuild, "gameBuild", null);
            if (Scribe.mode != LoadSaveMode.Saving)
            {
                var currentGameBuild = VersionControl.CurrentBuild + ":" + VersionControl.CurrentBuildDate + ":" + VersionControl.CurrentVersionStringWithRev;
                if (gameBuild == null || currentGameBuild != gameBuild)
                {
                    Log.Warning("GAGARIN: Game build changed " + gameBuild + " vs " + currentGameBuild + "; clearing cache.");
                    Context.IsUsingCache = false;
                    gameBuild = currentGameBuild;
                }
            }

            Scribe_Values.Look(ref GagarinPrefs.Enabled, "Enabled2", true);
            Scribe_Values.Look(ref GagarinPrefs.TextureCachingEnabled, "TextureCachingEnabled", true);
            Scribe_Values.Look(ref GagarinPrefs.FilterMode, "FilterMode", (int)UnityEngine.FilterMode.Trilinear);
            Scribe_Values.Look(ref GagarinPrefs.MipMapBias, "MipMapBias", float.MinValue);
            Scribe_Values.Look(ref GagarinPrefs.CacheExpires, "CacheExpires", true);
            Scribe_Values.Look(ref GagarinPrefs.CacheRetentionTime, "CacheRetentionTime", 14);
            Scribe_Values.Look(ref GagarinPrefs.MaxCacheGenerations, "MaxCacheGenerations", 3);
            Scribe_Values.Look(ref GagarinPrefs.TextureCacheMaxMB, "TextureCacheMaxMB", 4096);
            Scribe_Values.Look(ref GagarinPrefs.RecordAssetPipelineTelemetry, "RecordAssetPipelineTelemetry", true);

            if (Scribe.mode == LoadSaveMode.Saving)
                creationDateInt = GagarinPrefs.CacheCreationTime.ToString(FMT);

            Scribe_Values.Look(ref creationDateInt, "creationTime", DateTime.Now.ToString(FMT));
            if (Scribe.mode != LoadSaveMode.Saving && !string.IsNullOrEmpty(creationDateInt))
            {
                if (!DateTime.TryParseExact(creationDateInt, FMT, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeLocal, out GagarinPrefs.CacheCreationTime))
                    GagarinPrefs.CacheCreationTime = default(DateTime);
            }
        }

        public static void LoadSettings()
        {
            try
            {
                if (File.Exists(GagarinEnvironmentInfo.GagarinSettingsFilePath))
                {
                    Scribe.loader.InitLoading(GagarinEnvironmentInfo.GagarinSettingsFilePath);
                    try
                    {
                        Scribe_Deep.Look(ref Context.Settings, "ModSettings");
                        Context.Settings ??= new GagarinSettings();
                    }
                    catch (Exception er)
                    {
                        Log.Error("GAGARIN: Error while scribing settings " + er);
                        Logger.Debug("Error while scribing settings", exception: er);
                    }
                    finally
                    {
                        Scribe.loader.FinalizeLoading();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("GAGARIN: Caught exception while loading mod settings data for " + GagarinEnvironmentInfo.CacheFolderPath + ". Generating fresh settings. The exception was: " + ex);
                Context.Settings = null;
            }
            Context.Settings ??= new GagarinSettings();
            WriteSettings();
        }

        public static void WriteSettings()
        {
            Directory.CreateDirectory(GagarinEnvironmentInfo.CacheFolderPath);
            Directory.CreateDirectory(GagarinEnvironmentInfo.TexturesFolderPath);

            var destinationPath = GagarinEnvironmentInfo.GagarinSettingsFilePath;
            var temporaryPath = destinationPath + ".tmp";
            var backupPath = destinationPath + ".bak";
            Exception failure = null;
            var saverInitialized = false;

            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);

                Scribe.saver.InitSaving(temporaryPath, "SettingsBlock");
                saverInitialized = true;
                Scribe_Deep.Look(ref Context.Settings, "ModSettings");
            }
            catch (Exception er)
            {
                failure = er;
                Log.Error("GAGARIN: Error while scribing settings " + er);
                Logger.Debug("Error while scribing settings", exception: er);
            }
            finally
            {
                if (saverInitialized)
                {
                    try
                    {
                        Scribe.saver.FinalizeSaving();
                    }
                    catch (Exception er)
                    {
                        failure ??= er;
                        Log.Error("GAGARIN: Error while finalizing settings " + er);
                        Logger.Debug("Error while finalizing settings", exception: er);
                    }
                }
            }

            if (failure != null)
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
                return;
            }

            try
            {
                var verificationDocument = new XmlDocument();
                verificationDocument.Load(temporaryPath);

                if (File.Exists(destinationPath))
                {
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);

                    File.Replace(temporaryPath, destinationPath, backupPath, true);

                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                }
                else
                {
                    File.Move(temporaryPath, destinationPath);
                }
            }
            catch (Exception er)
            {
                Log.Error("GAGARIN: Could not activate newly written settings. The previous settings file was kept. " + er);
                Logger.Debug("Could not atomically activate Gagarin settings", exception: er);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
    }
}
