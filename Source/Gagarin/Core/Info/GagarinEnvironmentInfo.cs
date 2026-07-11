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
        private const string CacheSchemaFileName = "CacheSchema.version";
        private const string UnifiedXmlFileName = "Unified.xml";
        private const string UnifiedXmlHashFileName = "Unified.xml.sha256";
        private const string UnifiedPatchedOriginalXmlFileName = "Unified_Original.xml";
        private const string ModListFileName = "ModList.xml";
        private const string XmlFingerprintFileName = "ModFingerprint.xml";
        private const string TextureFingerprintFileName = "TextureFingerprint.xml";
        private const string HashFileName = "AssetsHash.xml";
        private const string HashIntFileName = "AssetsHashInt.xml";
        private const string GagarinSettingsFileName = "GagarinSettings.xml";

        private static string _cacheFolderPath;
        public static string CacheFolderPath =>
            _cacheFolderPath ??= Path.Combine(RocketEnvironmentInfo.CustomConfigFolderPath, CacheFolderName);

        private static string _cacheSchemaPath;
        public static string CacheSchemaFilePath =>
            _cacheSchemaPath ??= Path.Combine(CacheFolderPath, CacheSchemaFileName);

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

        private static string _unifiedXmlHashPath;
        public static string UnifiedXmlHashFilePath =>
            _unifiedXmlHashPath ??= Path.Combine(CacheFolderPath, UnifiedXmlHashFileName);

        private static string _unifiedPatchedOriginalXmlPath;
        public static string UnifiedPatchedOriginalXmlPath =>
            _unifiedPatchedOriginalXmlPath ??= Path.Combine(CacheFolderPath, UnifiedPatchedOriginalXmlFileName);

        private static string _modListPath;
        public static string ModListFilePath =>
            _modListPath ??= Path.Combine(CacheFolderPath, ModListFileName);

        private static string _xmlFingerprintPath;
        public static string XmlFingerprintFilePath =>
            _xmlFingerprintPath ??= Path.Combine(CacheFolderPath, XmlFingerprintFileName);

        public static string ModFingerprintFilePath => XmlFingerprintFilePath;

        private static string _textureFingerprintPath;
        public static string TextureFingerprintFilePath =>
            _textureFingerprintPath ??= Path.Combine(CacheFolderPath, TextureFingerprintFileName);

        private static string _hashFilePath;
        public static string HashFilePath =>
            _hashFilePath ??= Path.Combine(CacheFolderPath, HashFileName);

        private static string _hashFilePathInt;
        public static string HashFilePathInt =>
            _hashFilePathInt ??= Path.Combine(CacheFolderPath, HashIntFileName);

        public static bool CacheExists =>
            Directory.Exists(CacheFolderPath)
            && CacheSchemaUtility.IsCurrent(CacheSchemaFilePath)
            && File.Exists(UnifiedXmlFilePath)
            && File.Exists(UnifiedXmlHashFilePath)
            && File.Exists(HashFilePath)
            && File.Exists(HashFilePathInt)
            && File.Exists(ModListFilePath)
            && File.Exists(XmlFingerprintFilePath);

        private static bool _xmlInputsChangedInitialized;
        private static bool _xmlInputsChanged;
        public static bool XmlInputsChanged
        {
            get
            {
                if (_xmlInputsChangedInitialized)
                    return _xmlInputsChanged;

                _xmlInputsChangedInitialized = true;
                _xmlInputsChanged = RunningModsSetUtility.Changed(
                                        Context.RunningMods.Select(mod => mod.PackageId).ToList(), ModListFilePath)
                                    || ModFingerprintUtility.Changed(Context.RunningMods, XmlFingerprintFilePath,
                                        ModFingerprintDomain.Xml);
                return _xmlInputsChanged;
            }
        }

        private static bool _textureInputsChangedInitialized;
        private static bool _textureInputsChanged;
        public static bool TextureInputsChanged
        {
            get
            {
                if (_textureInputsChangedInitialized)
                    return _textureInputsChanged;

                _textureInputsChangedInitialized = true;
                _textureInputsChanged = ModFingerprintUtility.Changed(Context.RunningMods,
                    TextureFingerprintFilePath, ModFingerprintDomain.Textures);
                return _textureInputsChanged;
            }
        }

        public static bool ModListChanged => XmlInputsChanged;
    }
}
