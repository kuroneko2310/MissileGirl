using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;

namespace Gagarin
{
    public static class AssetPipelineCoordinator
    {
        private static readonly object Sync = new object();
        private static readonly GenerationStore Generations = new GenerationStore();
        private static readonly BlobStore Blobs = new BlobStore();
        private static AssetPipelineManifest currentManifest;
        private static AssetPipelineManifest previousManifest;
        private static AssetPipelinePolicyRegistry policies = new AssetPipelinePolicyRegistry();
        private static InvalidationPlan plan = new InvalidationPlan();
        private static bool initialized;
        private static bool restoredGeneration;
        private static string graphicsAlgorithmVersion = string.Empty;

        public static bool CanUseXmlCache { get; private set; }
        public static string ActiveGeneration => Generations.ActiveGenerationName ?? "none";
        public static string LastPlan => plan?.ToString() ?? "Not initialized";

        public static void Initialize(IReadOnlyList<ModContentPack> runningMods)
        {
            lock (Sync)
            {
                PipelineTelemetry.BeginStartup();
                Directory.CreateDirectory(GagarinEnvironmentInfo.CacheFolderPath);
                Directory.CreateDirectory(GagarinEnvironmentInfo.AssetPipelineFolderPath);
                Directory.CreateDirectory(GagarinEnvironmentInfo.GenerationsFolderPath);
                Directory.CreateDirectory(GagarinEnvironmentInfo.BlobsFolderPath);

                restoredGeneration = Generations.RestoreActiveLegacyView();
                previousManifest = LoadPreviousManifest();
                policies = AssetPipelinePolicyRegistry.Scan(runningMods);
                currentManifest = ModFingerprintBuilder.Build(runningMods, previousManifest);
                plan = currentManifest.CompareTo(previousManifest, policies);

                foreach (var packageId in plan.TextureMods)
                    Blobs.InvalidatePackage(packageId);

                if (plan.XmlDirty)
                {
                    Generations.InvalidateXmlView();
                    Context.IsUsingCache = false;
                }

                currentManifest.Save(GagarinEnvironmentInfo.PendingManifestFilePath);
                Blobs.PruneToBudget(GagarinPrefs.TextureCacheMaxMB * 1024L * 1024L);

                CanUseXmlCache = GagarinPrefs.Enabled && !plan.XmlDirty && restoredGeneration &&
                                 GagarinEnvironmentInfo.LegacyCacheFilesExist;
                initialized = true;
                PipelineTelemetry.RecordInitializationComplete();

                Log.Message($"GAGARIN: Asset Pipeline Manifest V2 initialized. Active generation={ActiveGeneration}; " +
                            $"CanUseXmlCache={CanUseXmlCache}; Plan={LastPlan}");
            }
        }

        public static void CommitXmlGeneration()
        {
            lock (Sync)
            {
                EnsureInitialized();
                if (currentManifest == null)
                    throw new InvalidOperationException("No current asset pipeline manifest is available.");

                currentManifest.CreatedUtcTicks = DateTime.UtcNow.Ticks;
                currentManifest.Save(GagarinEnvironmentInfo.PendingManifestFilePath);
                Generations.Commit(currentManifest, GagarinPrefs.MaxCacheGenerations);
                AtomicFile.Copy(GagarinEnvironmentInfo.PendingManifestFilePath, GagarinEnvironmentInfo.ManifestFilePath);
                previousManifest = currentManifest;
                plan = new InvalidationPlan();
                restoredGeneration = true;
                CanUseXmlCache = true;
            }
        }

        public static void OnCacheLoadFailure(string stage, Exception exception)
        {
            lock (Sync)
            {
                var replacementBuildFailure =
                    stage?.IndexOf("preparing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    stage?.IndexOf("saving", StringComparison.OrdinalIgnoreCase) >= 0;

                if (replacementBuildFailure)
                {
                    Log.Warning($"GAGARIN: Building a replacement cache failed while {stage}. The previous READY generation is preserved.\n{exception}");
                }
                else
                {
                    Log.Warning($"GAGARIN: Active cache generation failed while {stage}; marking it broken and selecting a previous generation for the next load.\n{exception}");
                    Generations.MarkActiveBroken(stage + ": " + exception?.Message);
                    Generations.TryRollback();
                }

                CanUseXmlCache = false;
                Context.IsUsingCache = false;
            }
        }

        public static void RequestXmlRebuild(string reason)
        {
            lock (Sync)
            {
                Generations.InvalidateXmlView();
                plan.Add(CacheDomain.Xml, reason ?? "Manual XML rebuild requested");
                CanUseXmlCache = false;
                Context.IsUsingCache = false;
            }
        }

        public static void RevalidateNow()
        {
            lock (Sync)
            {
                if (Context.RunningMods == null)
                    return;

                previousManifest = LoadPreviousManifest();
                policies = AssetPipelinePolicyRegistry.Scan(Context.RunningMods);
                currentManifest = ModFingerprintBuilder.Build(Context.RunningMods, previousManifest);
                plan = currentManifest.CompareTo(previousManifest, policies);
                currentManifest.Save(GagarinEnvironmentInfo.PendingManifestFilePath);

                foreach (var packageId in plan.TextureMods)
                    Blobs.InvalidatePackage(packageId);
                if (plan.XmlDirty)
                    RequestXmlRebuild("Manual revalidation: " + plan);
            }
        }

        public static void ClearXmlCache()
        {
            lock (Sync)
            {
                Generations.ClearAllGenerations();
                DeleteIfExists(GagarinEnvironmentInfo.ManifestFilePath);
                DeleteIfExists(GagarinEnvironmentInfo.PendingManifestFilePath);
                CanUseXmlCache = false;
                Context.IsUsingCache = false;
            }
        }

        public static void ClearTextureCache()
        {
            lock (Sync)
                Blobs.Clear();
        }

        public static void ClearAllCaches()
        {
            lock (Sync)
            {
                ClearXmlCache();
                ClearTextureCache();
            }
        }

        public static void PruneOldData()
        {
            lock (Sync)
            {
                Generations.Prune(GagarinPrefs.MaxCacheGenerations);
                Blobs.PruneToBudget(GagarinPrefs.TextureCacheMaxMB * 1024L * 1024L);
            }
        }

        public static string GetStatusText()
        {
            lock (Sync)
            {
                return $"Generation: {ActiveGeneration}\n" +
                       $"Manifest: {(currentManifest == null ? "not loaded" : "v" + currentManifest.SchemaVersion)}\n" +
                       $"Invalidation: {LastPlan}\n" +
                       $"Texture blobs: {Blobs.GetStatistics()}";
            }
        }

        public static void RecordStartupComplete()
        {
            lock (Sync)
            {
                PipelineTelemetry.RecordStartupComplete(
                    Context.IsUsingCache,
                    ActiveGeneration,
                    LastPlan,
                    Blobs.GetStatistics());
            }
        }

        internal static bool TryGetTextureBlob(string key, out string path)
        {
            if (!GagarinPrefs.TextureCachingEnabled)
            {
                path = null;
                return false;
            }
            return Blobs.TryGet(key, out path);
        }

        internal static bool StoreTextureBlob(
            string key,
            byte[] payload,
            string sourcePath,
            string packageId,
            string profile)
        {
            return GagarinPrefs.TextureCachingEnabled &&
                   Blobs.Store(key, payload, sourcePath, packageId, profile);
        }

        internal static void InvalidateTextureSource(string sourcePath) => Blobs.InvalidateSource(sourcePath);
        internal static void InvalidateTexturePackage(string packageId) => Blobs.InvalidatePackage(packageId);
        internal static string GetTextureStatistics() => Blobs.GetStatistics();
        internal static string GetTexturePolicy(string packageId, string texturePath) => policies?.FindTexturePolicy(packageId, texturePath);

        internal static void SetGraphicsAlgorithmVersion(string version)
        {
            graphicsAlgorithmVersion = version ?? string.Empty;
        }

        internal static string GetGraphicsAlgorithmVersion() => graphicsAlgorithmVersion;

        internal static string GetSourceContentHash(string sourcePath)
        {
            var normalized = PipelineHash.NormalizePath(sourcePath);
            foreach (var mod in currentManifest?.Mods ?? Enumerable.Empty<ModFingerprint>())
            {
                if (string.IsNullOrEmpty(mod.RootPath) || !normalized.StartsWith(mod.RootPath + "/", StringComparison.OrdinalIgnoreCase))
                    continue;
                var relative = normalized.Substring(mod.RootPath.Length + 1);
                if (mod.Files.TryGetValue(relative, out var file) && !string.IsNullOrEmpty(file.StrongHash))
                    return file.StrongHash;
            }
            return string.Empty;
        }

        internal static string ResolvePackageIdForPath(string sourcePath)
        {
            var normalized = PipelineHash.NormalizePath(sourcePath);
            var bestLength = -1;
            string result = string.Empty;
            foreach (var mod in currentManifest?.Mods ?? Enumerable.Empty<ModFingerprint>())
            {
                if (string.IsNullOrEmpty(mod.RootPath) || !normalized.StartsWith(mod.RootPath + "/", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (mod.RootPath.Length <= bestLength)
                    continue;
                bestLength = mod.RootPath.Length;
                result = mod.PackageId;
            }
            return result;
        }

        private static AssetPipelineManifest LoadPreviousManifest()
        {
            var generationManifest = Generations.GetActiveManifestPath();
            foreach (var path in new[] { generationManifest, GagarinEnvironmentInfo.ManifestFilePath })
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    continue;
                try
                {
                    return AssetPipelineManifest.Load(path);
                }
                catch (Exception exception)
                {
                    Log.Warning($"GAGARIN: Could not load Manifest V2 '{path}'. A rebuild will be performed.\n{exception}");
                }
            }
            return null;
        }

        private static void EnsureInitialized()
        {
            if (!initialized)
                throw new InvalidOperationException("AssetPipelineCoordinator.Initialize must run before this operation.");
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    public static class TextureCacheBridge
    {
        public const int BridgeSchemaVersion = 2;

        public static bool IsAvailable() => true;
        public static int GetBridgeSchemaVersion() => BridgeSchemaVersion;
        public static string GetCacheRoot() => GagarinEnvironmentInfo.AssetPipelineFolderPath;
        public static bool TryGetTextureBlob(string key, out string path) => AssetPipelineCoordinator.TryGetTextureBlob(key, out path);
        public static bool StoreTextureBlob(string key, byte[] payload, string sourcePath, string packageId, string profile)
            => AssetPipelineCoordinator.StoreTextureBlob(key, payload, sourcePath, packageId, profile);
        public static void InvalidateTextureSource(string sourcePath) => AssetPipelineCoordinator.InvalidateTextureSource(sourcePath);
        public static void InvalidateTexturePackage(string packageId) => AssetPipelineCoordinator.InvalidateTexturePackage(packageId);
        public static string GetStatistics() => AssetPipelineCoordinator.GetTextureStatistics();
        public static string GetTexturePolicy(string packageId, string texturePath)
            => AssetPipelineCoordinator.GetTexturePolicy(packageId, texturePath);
        public static string ResolvePackageIdForPath(string sourcePath)
            => AssetPipelineCoordinator.ResolvePackageIdForPath(sourcePath);
        public static string GetSourceContentHash(string sourcePath)
            => AssetPipelineCoordinator.GetSourceContentHash(sourcePath);
        public static void SetGraphicsAlgorithmVersion(string version)
            => AssetPipelineCoordinator.SetGraphicsAlgorithmVersion(version);
    }
}
