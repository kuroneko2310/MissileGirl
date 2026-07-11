using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Verse;

namespace Gagarin
{
    internal sealed class TexturePolicyRule
    {
        public string PackageId;
        public string PathPrefix;
        public string Category;
        public int MaxDimension;
        public string MipPolicy;
        public string Format;
        public bool? Linear;

        public string Serialize()
        {
            return string.Join(";", new[]
            {
                "packageId=" + (PackageId ?? string.Empty),
                "pathPrefix=" + (PathPrefix ?? string.Empty),
                "category=" + (Category ?? string.Empty),
                "maxDimension=" + MaxDimension,
                "mipPolicy=" + (MipPolicy ?? string.Empty),
                "format=" + (Format ?? string.Empty),
                "linear=" + (Linear.HasValue ? Linear.Value.ToString() : string.Empty)
            });
        }
    }

    internal sealed class AssetPipelinePolicyRegistry
    {
        public readonly HashSet<string> XmlCacheUnsafePackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> TextureDirtyPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<TexturePolicyRule> TexturePolicies = new List<TexturePolicyRule>();

        public static AssetPipelinePolicyRegistry Scan(IEnumerable<ModContentPack> mods)
        {
            var registry = new AssetPipelinePolicyRegistry();
            foreach (var mod in mods ?? Enumerable.Empty<ModContentPack>())
                registry.ScanMod(mod);
            return registry;
        }

        public string FindTexturePolicy(string packageId, string texturePath)
        {
            var normalizedPath = (texturePath ?? string.Empty).Replace('\\', '/');
            var matching = TexturePolicies
                .Where(rule => string.IsNullOrEmpty(rule.PackageId) ||
                               string.Equals(rule.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
                .Where(rule => string.IsNullOrEmpty(rule.PathPrefix) ||
                               normalizedPath.StartsWith(rule.PathPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(rule => rule.PathPrefix?.Length ?? 0)
                .FirstOrDefault();
            return matching?.Serialize();
        }

        private void ScanMod(ModContentPack mod)
        {
            if (mod == null || string.IsNullOrEmpty(mod.RootDir))
                return;

            var extras = Path.Combine(mod.RootDir, "Extras");
            if (!Directory.Exists(extras))
                return;

            foreach (var path in Directory.EnumerateFiles(extras, "*.xml", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(path);
                if (name == null || !name.StartsWith("Rocket", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var document = new XmlDocument { XmlResolver = null };
                    var settings = new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Prohibit,
                        IgnoreComments = true,
                        IgnoreWhitespace = true,
                        XmlResolver = null
                    };
                    using (var reader = XmlReader.Create(path, settings))
                        document.Load(reader);

                    var root = document.DocumentElement;
                    if (root == null || root.Name != "RocketRules")
                        continue;

                    foreach (XmlNode child in root.ChildNodes)
                    {
                        if (!(child is XmlElement element))
                            continue;
                        ProcessRule(element, mod.PackageId);
                    }
                }
                catch (Exception exception)
                {
                    Log.Warning($"GAGARIN: Could not parse asset-pipeline RocketRules file '{path}'.\n{exception}");
                }
            }
        }

        private void ProcessRule(XmlElement element, string ownerPackageId)
        {
            var packageId = element.GetAttribute("packageId");
            if (string.IsNullOrWhiteSpace(packageId))
                packageId = ownerPackageId;

            switch (element.Name)
            {
                case "XmlCacheUnsafe":
                    if (!string.IsNullOrEmpty(packageId))
                        XmlCacheUnsafePackages.Add(packageId);
                    break;
                case "TextureDirty":
                    if (!string.IsNullOrEmpty(packageId))
                        TextureDirtyPackages.Add(packageId);
                    break;
                case "TexturePolicy":
                    var rule = new TexturePolicyRule
                    {
                        PackageId = packageId,
                        PathPrefix = element.GetAttribute("pathPrefix").Replace('\\', '/'),
                        Category = element.GetAttribute("category"),
                        MipPolicy = element.GetAttribute("mipPolicy"),
                        Format = element.GetAttribute("format")
                    };
                    if (int.TryParse(element.GetAttribute("maxDimension"), out var maxDimension))
                        rule.MaxDimension = Math.Max(0, maxDimension);
                    if (bool.TryParse(element.GetAttribute("linear"), out var linear))
                        rule.Linear = linear;
                    TexturePolicies.Add(rule);
                    break;
            }
        }
    }
}
