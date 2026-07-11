using System.IO;
using System.Linq;
using MissileGirl;

namespace Gagarin
{
    public class GagarinEnvironmentInfo
    {
        private const string CacheFolderName = "Cache";
        private const string TexturesFolderName = "Textures";
        private const string ReportsFolderName = "Reports";
        private const string UnifiedXmlFileName = "Unified.xml";
        private const string UnifiedPatchedOriginalXmlFileName = "Unified_Original.xml";
        private const string ModListFileName = "ModList.xml";
        private const string ModFingerprintFileName = "ModFingerprint.xml";
        private const string HashFileName = "AssetsHash.xml";
        private const string HashIntFileName = "AssetsHashInt.xml";
        private const string GagarinSettingsFileName = "GagarinSettings.xml";

        private static string _cacheFolderPath;
        public static string CacheFolderPath =>
            _cacheFolderPath ??= Path.Combine(RocketEnvironmentInfo.CustomConfigFolderPath, CacheFolderName);

        private static string _gagarinSettingsPath;
        public static string GagarinSettingsFilePath =>
            _gagarinSettingsPath ??= Path.Combine(CacheFolderPath, GagarinSettingsFileName);

        private static string _texturesFolderPath;
        public static string TexturesFolderPath =>
            _texturesFolderPath ??= Path.Combine(CacheFolderPath, TexturesFolderName);

        private static string _reportsFolderPath;
        public static string ReportsFolderPath =>
            _reportsFolderPath ??= Path.Combine(RocketEnvironmentInfo.CustomConfigFolderPath, ReportsFolderName);

        private static string _unifiedXmlPath;
        public static string UnifiedXmlFilePath =>
            _unifiedXmlPath ??= Path.Combine(CacheFolderPath, UnifiedXmlFileName);

        private static string _unifiedPatchedOriginalXmlPath;
        public static string UnifiedPatchedOriginalXmlPath =>
            _unifiedPatchedOriginalXmlPath ??= Path.Combine(CacheFolderPath, UnifiedPatchedOriginalXmlFileName);

        private static string _modListPath;
        public static string ModListFilePath =>
            _modListPath ??= Path.Combine(CacheFolderPath, ModListFileName);

        private static string _modFingerprintPath;
        public static string ModFingerprintFilePath =>
            _modFingerprintPath ??= Path.Combine(CacheFolderPath, ModFingerprintFileName);

        private static string _hashFilePath;
        public static string HashFilePath =>
            _hashFilePath ??= Path.Combine(CacheFolderPath, HashFileName);

        private static string _hashFilePathInt;
        public static string HashFilePathInt =>
            _hashFilePathInt ??= Path.Combine(CacheFolderPath, HashIntFileName);

        public static bool CacheExists =>
            Directory.Exists(CacheFolderPath)
            && File.Exists(UnifiedXmlFilePath)
            && File.Exists(HashFilePath)
            && File.Exists(HashFilePathInt)
            && File.Exists(ModListFilePath)
            && File.Exists(ModFingerprintFilePath);

        private static bool _modListChangedInitialized;
        private static bool _modListChanged;

        public static bool ModListChanged
        {
            get
            {
                if (_modListChangedInitialized)
                    return _modListChanged;

                _modListChangedInitialized = true;
                _modListChanged = RunningModsSetUtility.Changed(
                                      Context.RunningMods.Select(mod => mod.PackageId).ToList(), ModListFilePath)
                                  || ModFingerprintUtility.Changed(Context.RunningMods, ModFingerprintFilePath);
                return _modListChanged;
            }
        }
    }
}
