using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;

namespace Gagarin
{
    [Flags]
    public enum CacheDomain
    {
        None = 0,
        Xml = 1,
        Code = 2,
        Texture = 4,
        Metadata = 8,
        LoadOrder = 16,
        GameBuild = 32,
        Algorithm = 64,
        All = Xml | Code | Texture | Metadata | LoadOrder | GameBuild | Algorithm
    }

    internal sealed class FileFingerprint
    {
        public string RelativePath;
        public long Size;
        public long LastWriteUtcTicks;
        public string StrongHash;
        public CacheDomain Domain;

        public string StableValue => $"{RelativePath}|{Size}|{StrongHash}|{(int)Domain}";
    }

    internal sealed class ModFingerprint
    {
        public string PackageId;
        public string RootPath;
        public string XmlSignature;
        public string CodeSignature;
        public string TextureSignature;
        public string MetadataSignature;
        public readonly Dictionary<string, FileFingerprint> Files =
            new Dictionary<string, FileFingerprint>(StringComparer.OrdinalIgnoreCase);

        public string CombinedSignature => PipelineHash.Aggregate(new[]
        {
            PackageId ?? string.Empty,
            RootPath ?? string.Empty,
            XmlSignature ?? string.Empty,
            CodeSignature ?? string.Empty,
            TextureSignature ?? string.Empty,
            MetadataSignature ?? string.Empty
        });
    }

    internal sealed class InvalidationPlan
    {
        public CacheDomain Domains;
        public readonly HashSet<string> XmlMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> TextureMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Reasons = new List<string>();

        public bool XmlDirty => (Domains & (CacheDomain.Xml | CacheDomain.Code | CacheDomain.Metadata |
                                            CacheDomain.LoadOrder | CacheDomain.GameBuild | CacheDomain.Algorithm)) != 0;
        public bool TextureDirty => (Domains & CacheDomain.Texture) != 0;
        public bool Any => Domains != CacheDomain.None;

        public void Add(CacheDomain domain, string reason, string packageId = null)
        {
            Domains |= domain;
            if (!string.IsNullOrEmpty(reason) && !Reasons.Contains(reason))
                Reasons.Add(reason);

            if (!string.IsNullOrEmpty(packageId))
            {
                if ((domain & (CacheDomain.Xml | CacheDomain.Code | CacheDomain.Metadata)) != 0)
                    XmlMods.Add(packageId);
                if ((domain & CacheDomain.Texture) != 0)
                    TextureMods.Add(packageId);
            }
        }

        public override string ToString()
        {
            if (!Any)
                return "No cache changes detected";
            return $"{Domains}: {string.Join("; ", Reasons)}";
        }
    }

    internal sealed class AssetPipelineManifest
    {
        public const int CurrentSchemaVersion = 2;
        public const string CurrentAlgorithmVersion = "gagarin-manifest-v2.1";

        public int SchemaVersion = CurrentSchemaVersion;
        public string AlgorithmVersion = CurrentAlgorithmVersion;
        public string GameBuild;
        public string LoadOrderSignature;
        public long CreatedUtcTicks;
        public readonly List<ModFingerprint> Mods = new List<ModFingerprint>();

        public ModFingerprint FindMod(string packageId)
        {
            return Mods.FirstOrDefault(mod => string.Equals(mod.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
        }

        public void Save(string path)
        {
            var document = new XmlDocument();
            var root = document.CreateElement("AssetPipelineManifest");
            root.SetAttribute("schema", SchemaVersion.ToString(CultureInfo.InvariantCulture));
            root.SetAttribute("algorithm", AlgorithmVersion ?? string.Empty);
            root.SetAttribute("gameBuild", GameBuild ?? string.Empty);
            root.SetAttribute("loadOrder", LoadOrderSignature ?? string.Empty);
            root.SetAttribute("createdUtcTicks", CreatedUtcTicks.ToString(CultureInfo.InvariantCulture));
            document.AppendChild(root);

            foreach (var mod in Mods.OrderBy(value => value.PackageId, StringComparer.OrdinalIgnoreCase))
            {
                var modNode = document.CreateElement("Mod");
                modNode.SetAttribute("packageId", mod.PackageId ?? string.Empty);
                modNode.SetAttribute("root", mod.RootPath ?? string.Empty);
                modNode.SetAttribute("xml", mod.XmlSignature ?? string.Empty);
                modNode.SetAttribute("code", mod.CodeSignature ?? string.Empty);
                modNode.SetAttribute("texture", mod.TextureSignature ?? string.Empty);
                modNode.SetAttribute("metadata", mod.MetadataSignature ?? string.Empty);
                root.AppendChild(modNode);

                foreach (var file in mod.Files.Values.OrderBy(value => value.RelativePath, StringComparer.OrdinalIgnoreCase))
                {
                    var fileNode = document.CreateElement("File");
                    fileNode.SetAttribute("path", file.RelativePath ?? string.Empty);
                    fileNode.SetAttribute("size", file.Size.ToString(CultureInfo.InvariantCulture));
                    fileNode.SetAttribute("writeTicks", file.LastWriteUtcTicks.ToString(CultureInfo.InvariantCulture));
                    fileNode.SetAttribute("hash", file.StrongHash ?? string.Empty);
                    fileNode.SetAttribute("domain", ((int)file.Domain).ToString(CultureInfo.InvariantCulture));
                    modNode.AppendChild(fileNode);
                }
            }

            AtomicFile.SaveXml(path, document);
        }

        public static AssetPipelineManifest Load(string path)
        {
            if (!File.Exists(path))
                return null;

            var document = new XmlDocument();
            var settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            using (var reader = XmlReader.Create(path, settings))
                document.Load(reader);

            var root = document.DocumentElement;
            if (root == null || root.Name != "AssetPipelineManifest")
                throw new InvalidDataException("Asset pipeline manifest has an invalid root element.");

            var manifest = new AssetPipelineManifest
            {
                SchemaVersion = ParseInt(root.GetAttribute("schema"), 0),
                AlgorithmVersion = root.GetAttribute("algorithm"),
                GameBuild = root.GetAttribute("gameBuild"),
                LoadOrderSignature = root.GetAttribute("loadOrder"),
                CreatedUtcTicks = ParseLong(root.GetAttribute("createdUtcTicks"), 0L)
            };

            foreach (XmlNode child in root.ChildNodes)
            {
                if (!(child is XmlElement modNode) || modNode.Name != "Mod")
                    continue;

                var mod = new ModFingerprint
                {
                    PackageId = modNode.GetAttribute("packageId"),
                    RootPath = modNode.GetAttribute("root"),
                    XmlSignature = modNode.GetAttribute("xml"),
                    CodeSignature = modNode.GetAttribute("code"),
                    TextureSignature = modNode.GetAttribute("texture"),
                    MetadataSignature = modNode.GetAttribute("metadata")
                };

                foreach (XmlNode fileChild in modNode.ChildNodes)
                {
                    if (!(fileChild is XmlElement fileNode) || fileNode.Name != "File")
                        continue;

                    var file = new FileFingerprint
                    {
                        RelativePath = fileNode.GetAttribute("path"),
                        Size = ParseLong(fileNode.GetAttribute("size"), -1L),
                        LastWriteUtcTicks = ParseLong(fileNode.GetAttribute("writeTicks"), 0L),
                        StrongHash = fileNode.GetAttribute("hash"),
                        Domain = (CacheDomain)ParseInt(fileNode.GetAttribute("domain"), 0)
                    };
                    if (!string.IsNullOrEmpty(file.RelativePath))
                        mod.Files[file.RelativePath] = file;
                }

                manifest.Mods.Add(mod);
            }

            return manifest;
        }

        public InvalidationPlan CompareTo(AssetPipelineManifest previous, AssetPipelinePolicyRegistry policies)
        {
            var plan = new InvalidationPlan();
            if (previous == null)
            {
                plan.Add(CacheDomain.All, "No previous Manifest V2 exists");
                foreach (var mod in Mods)
                {
                    plan.XmlMods.Add(mod.PackageId);
                    plan.TextureMods.Add(mod.PackageId);
                }
                return plan;
            }

            if (previous.SchemaVersion != CurrentSchemaVersion)
                plan.Add(CacheDomain.Algorithm, $"Manifest schema changed from {previous.SchemaVersion} to {CurrentSchemaVersion}");
            if (!string.Equals(previous.AlgorithmVersion, AlgorithmVersion, StringComparison.Ordinal))
                plan.Add(CacheDomain.Algorithm, "Cache algorithm version changed");
            if (!string.Equals(previous.GameBuild, GameBuild, StringComparison.Ordinal))
                plan.Add(CacheDomain.GameBuild, "RimWorld build changed");
            if (!string.Equals(previous.LoadOrderSignature, LoadOrderSignature, StringComparison.Ordinal))
                plan.Add(CacheDomain.LoadOrder, "Active mod order changed");

            var currentById = Mods.ToDictionary(mod => mod.PackageId, StringComparer.OrdinalIgnoreCase);
            var previousById = previous.Mods.ToDictionary(mod => mod.PackageId, StringComparer.OrdinalIgnoreCase);
            foreach (var packageId in currentById.Keys.Union(previousById.Keys, StringComparer.OrdinalIgnoreCase))
            {
                if (!currentById.TryGetValue(packageId, out var current) ||
                    !previousById.TryGetValue(packageId, out var old))
                {
                    plan.Add(CacheDomain.Xml | CacheDomain.Texture | CacheDomain.Metadata,
                        $"Mod was added or removed: {packageId}", packageId);
                    continue;
                }

                if (!string.Equals(current.RootPath, old.RootPath, StringComparison.OrdinalIgnoreCase))
                    plan.Add(CacheDomain.Metadata, $"Mod root moved: {packageId}", packageId);
                if (!string.Equals(current.XmlSignature, old.XmlSignature, StringComparison.Ordinal))
                    plan.Add(CacheDomain.Xml, $"Defs/Patches changed: {packageId}", packageId);
                if (!string.Equals(current.CodeSignature, old.CodeSignature, StringComparison.Ordinal))
                    plan.Add(CacheDomain.Code, $"Assemblies changed: {packageId}", packageId);
                if (!string.Equals(current.MetadataSignature, old.MetadataSignature, StringComparison.Ordinal))
                    plan.Add(CacheDomain.Metadata, $"About/LoadFolders changed: {packageId}", packageId);
                if (!string.Equals(current.TextureSignature, old.TextureSignature, StringComparison.Ordinal))
                    plan.Add(CacheDomain.Texture, $"Textures changed: {packageId}", packageId);
            }

            if (policies != null)
            {
                foreach (var packageId in policies.XmlCacheUnsafePackages)
                    plan.Add(CacheDomain.Xml, $"RocketRules XmlCacheUnsafe: {packageId}", packageId);
                foreach (var packageId in policies.TextureDirtyPackages)
                    plan.Add(CacheDomain.Texture, $"RocketRules TextureDirty: {packageId}", packageId);
            }

            return plan;
        }

        private static int ParseInt(string value, int fallback)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : fallback;
        }

        private static long ParseLong(string value, long fallback)
        {
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : fallback;
        }
    }
}
