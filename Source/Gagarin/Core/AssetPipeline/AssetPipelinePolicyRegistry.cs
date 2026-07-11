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
            var normalizedPath = NormalizePath(texturePath);
            var matching = TexturePolicies
                .Where(rule => string.IsNullOrEmpty(rule.PackageId) ||
                               string.Equals(rule.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
                .Where(rule => PathPrefixMatches(normalizedPath, rule.PathPrefix))
                .OrderByDescending(rule => NormalizePath(rule.PathPrefix).Length)
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

            foreach (var path in EnumerateRuleFiles(extras))
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

        private static IEnumerable<string> EnumerateRuleFiles(string root)
        {
            var pending = new Stack<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            pending.Push(root);

            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                string normalized;
                try
                {
                    normalized = PipelineHash.NormalizePath(directory);
                }
                catch
                {
                    normalized = directory;
                }

                if (!visited.Add(normalized))
                    continue;

                string[] files;
                try
                {
                    files = Directory.GetFiles(directory);
                }
                catch (Exception exception)
                {
                    Log.Warning($"GAGARIN: Could not enumerate RocketRules files under '{directory}'.\n{exception}");
                    files = Array.Empty<string>();
                }

                foreach (var file in files)
                {
                    if (string.Equals(Path.GetExtension(file), ".xml", StringComparison.OrdinalIgnoreCase))
                        yield return file;
                }

                string[] directories;
                try
                {
                    directories = Directory.GetDirectories(directory);
                }
                catch (Exception exception)
                {
                    Log.Warning($"GAGARIN: Could not enumerate RocketRules directories under '{directory}'.\n{exception}");
                    continue;
                }

                foreach (var child in directories)
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                            pending.Push(child);
                    }
                    catch (Exception exception)
                    {
                        Log.Warning($"GAGARIN: Could not inspect RocketRules directory '{child}'.\n{exception}");
                    }
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
                        PathPrefix = NormalizePath(element.GetAttribute("pathPrefix")),
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

        private static bool PathPrefixMatches(string normalizedPath, string rawPrefix)
        {
            var prefix = NormalizePath(rawPrefix).Trim('/');
            if (string.IsNullOrEmpty(prefix))
                return true;
            if (string.IsNullOrEmpty(normalizedPath))
                return false;

            var path = normalizedPath.Trim('/');
            if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                return true;

            var marker = "/" + prefix;
            var searchFrom = 0;
            while (searchFrom < path.Length)
            {
                var index = path.IndexOf(marker, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    return false;

                var end = index + marker.Length;
                if (end == path.Length || path[end] == '/')
                    return true;
                searchFrom = index + 1;
            }

            return false;
        }

        private static string NormalizePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').Trim();
        }
    }
}
