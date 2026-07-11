// // Copyright (c) 2026 ViralReaction
// //
// // This program and the accompanying materials are made available under the
// // terms of the Eclipse Public License 2.0 which is available at
// // http://www.eclipse.org/legal/epl-2.0.
// //
// // SPDX-License-Identifier: EPL-2.0

using System.IO;
using System.Linq;
using MissileGirl;

namespace Gagarin
{
    public static class GagarinEnvironmentInfo
    {
        private const string CacheFolderName = "Cache";
        private const string TexturesFolderName = "Textures";
        private const string ReportsFolderName = "Reports";
        private const string AssetPipelineFolderName = "AssetPipeline";

        public static string CacheFolderPath => Path.Combine(RocketEnvironmentInfo.CustomConfigFolderPath, CacheFolderName);
        public static string GagarinSettingsFilePath => Path.Combine(CacheFolderPath, "GagarinSettings.xml");
        public static string TexturesFolderPath => Path.Combine(CacheFolderPath, TexturesFolderName);
        public static string ReportsFolderPath => Path.Combine(RocketEnvironmentInfo.CustomConfigFolderPath, ReportsFolderName);
        public static string UnifiedXmlFilePath => Path.Combine(CacheFolderPath, "Unified.xml");
        public static string UnifiedPatchedOriginalXmlPath => Path.Combine(CacheFolderPath, "Unified_Original.xml");
        public static string ModListFilePath => Path.Combine(CacheFolderPath, "ModList.xml");
        public static string HashFilePath => Path.Combine(CacheFolderPath, "AssetsHash.xml");
        public static string HashFilePathInt => Path.Combine(CacheFolderPath, "AssetsHashInt.xml");

        public static string AssetPipelineFolderPath => Path.Combine(CacheFolderPath, AssetPipelineFolderName);
        public static string GenerationsFolderPath => Path.Combine(AssetPipelineFolderPath, "generations");
        public static string BlobsFolderPath => Path.Combine(AssetPipelineFolderPath, "blobs");
        public static string ActiveGenerationFilePath => Path.Combine(AssetPipelineFolderPath, "active.txt");
        public static string ManifestFilePath => Path.Combine(AssetPipelineFolderPath, "manifest.xml");
        public static string PendingManifestFilePath => Path.Combine(AssetPipelineFolderPath, "manifest.pending.xml");
        public static string BlobIndexFilePath => Path.Combine(AssetPipelineFolderPath, "blob-index.xml");
        public static string TelemetryFilePath => Path.Combine(AssetPipelineFolderPath, "telemetry.xml");

        public static bool LegacyCacheFilesExist =>
            File.Exists(UnifiedXmlFilePath) &&
            File.Exists(HashFilePath) &&
            File.Exists(HashFilePathInt) &&
            Directory.Exists(CacheFolderPath);

        public static bool CacheExists => LegacyCacheFilesExist;

        public static bool ModListChanged => RunningModsSetUtility.Changed(
            Context.RunningMods.Select(mod => mod.PackageId).ToList(),
            ModListFilePath);
    }
}
