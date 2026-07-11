using System;
using System.Globalization;
using System.IO;
using MissileGirl;
using RimWorld;
using Verse;

namespace Gagarin
{
    public class GagarinSettings : IExposable
    {
        private const string DateFormat = "yyyy-MM-dd HH:mm:ss.fffffff";

        private string creationDateInt;
        private string gameBuild;

        public void ExposeData()
        {
            string currentGameBuild =
                $"{VersionControl.CurrentBuild}:{VersionControl.CurrentBuildDate}:{VersionControl.CurrentVersionStringWithRev}";

            if (Prefs.LogVerbose)
                Log.Message($"b.{currentGameBuild}");

            if (Scribe.mode == LoadSaveMode.Saving)
                gameBuild = currentGameBuild;

            Scribe_Values.Look(ref gameBuild, "gameBuild", null);
            if (Scribe.mode != LoadSaveMode.Saving && (gameBuild == null || currentGameBuild != gameBuild))
            {
                Log.Warning($"GAGARIN: Game build changed {gameBuild} vs {currentGameBuild}; clearing XML cache");
                GagarinCacheManager.InvalidateXmlCache("RimWorld game build changed");
                Context.IsUsingCache = false;
                gameBuild = currentGameBuild;
            }

            Scribe_Values.Look(ref GagarinPrefs.Enabled, "Enabled2", true);
            Scribe_Values.Look(ref GagarinPrefs.TextureCachingEnabled, "TextureCachingEnabled", false);
            Scribe_Values.Look(ref GagarinPrefs.FilterMode, "FilterMode", (int)UnityEngine.FilterMode.Trilinear);
            Scribe_Values.Look(ref GagarinPrefs.MipMapBias, "MipMapBias", float.MinValue);
            Scribe_Values.Look(ref GagarinPrefs.CacheExpires, "CacheExpires", true);
            Scribe_Values.Look(ref GagarinPrefs.CacheRetentionTime, "CacheRetentionTime", 3);

            if (Scribe.mode == LoadSaveMode.Saving)
                creationDateInt = GagarinPrefs.CacheCreationTime.ToString(DateFormat, CultureInfo.InvariantCulture);

            Scribe_Values.Look(ref creationDateInt, "creationTime", DateTime.Now.ToString(DateFormat,
                CultureInfo.InvariantCulture));

            if (Scribe.mode != LoadSaveMode.Saving && creationDateInt != null)
            {
                if (DateTime.TryParseExact(creationDateInt, DateFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeLocal, out DateTime creationTime))
                    GagarinPrefs.CacheCreationTime = creationTime;
                else
                    GagarinPrefs.CacheCreationTime = default;
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
                    catch (Exception exception)
                    {
                        Log.Error($"GAGARIN: Error while scribing settings {exception}");
                        Logger.Debug("Error while scribing settings", exception: exception);
                        Context.Settings = null;
                    }
                    finally
                    {
                        Scribe.loader.FinalizeLoading();
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Error($"GAGARIN: Caught exception while loading settings for {GagarinEnvironmentInfo.CacheFolderPath}. Generating fresh settings. {exception}");
                Context.Settings = null;
            }

            Context.Settings ??= new GagarinSettings();
            WriteSettings();
        }

        public static void WriteSettings()
        {
            Directory.CreateDirectory(GagarinEnvironmentInfo.CacheFolderPath);
            Directory.CreateDirectory(GagarinEnvironmentInfo.TexturesFolderPath);

            AtomicFile.Write(GagarinEnvironmentInfo.GagarinSettingsFilePath, temporaryPath =>
            {
                Scribe.saver.InitSaving(temporaryPath, "SettingsBlock");
                try
                {
                    Scribe_Deep.Look(ref Context.Settings, "ModSettings");
                }
                catch (Exception exception)
                {
                    Log.Error($"GAGARIN: Error while scribing settings {exception}");
                    Logger.Debug("Error while scribing settings", exception: exception);
                    throw;
                }
                finally
                {
                    Scribe.saver.FinalizeSaving();
                }
            });
        }
    }
}
