using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;

namespace Gagarin
{
    internal static class ModFingerprintBuilder
    {
        private static readonly HashSet<string> TextureExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".dds", ".jpg", ".jpeg", ".tga", ".bmp", ".psd"
        };

        public static AssetPipelineManifest Build(
            IReadOnlyList<ModContentPack> runningMods,
            AssetPipelineManifest previous)
        {
            var manifest = new AssetPipelineManifest
            {
                CreatedUtcTicks = DateTime.UtcNow.Ticks,
                GameBuild = $"{VersionControl.CurrentBuild}:{VersionControl.CurrentBuildDate}:{VersionControl.CurrentVersionStringWithRev}",
                LoadOrderSignature = PipelineHash.Aggregate(runningMods.Select((mod, index) =>
                    $"{index:D5}|{mod.PackageId ?? string.Empty}"))
            };

            foreach (var mod in runningMods)
            {
                var previousMod = previous?.FindMod(mod.PackageId);
                manifest.Mods.Add(BuildMod(mod, previousMod));
            }

            return manifest;
        }

        private static ModFingerprint BuildMod(ModContentPack mod, ModFingerprint previous)
        {
            var root = mod?.RootDir;
            var result = new ModFingerprint
            {
                PackageId = mod?.PackageId ?? string.Empty,
                RootPath = PipelineHash.NormalizePath(root)
            };

            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                result.MetadataSignature = PipelineHash.TextSha256("missing-root:" + result.RootPath);
                result.XmlSignature = PipelineHash.TextSha256(string.Empty);
                result.CodeSignature = PipelineHash.TextSha256(string.Empty);
                result.TextureSignature = PipelineHash.TextSha256(string.Empty);
                return result;
            }

            foreach (var path in EnumerateRelevantFiles(root))
            {
                try
                {
                    var info = new FileInfo(path);
                    var relative = GetRelativePath(root, path);
                    var domain = Classify(relative);
                    if (domain == CacheDomain.None)
                        continue;

                    string strongHash;
                    if (previous != null && previous.Files.TryGetValue(relative, out var old) &&
                        old.Size == info.Length && old.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks &&
                        !string.IsNullOrEmpty(old.StrongHash))
                    {
                        strongHash = old.StrongHash;
                    }
                    else
                    {
                        strongHash = PipelineHash.FileSha256(path);
                    }

                    result.Files[relative] = new FileFingerprint
                    {
                        RelativePath = relative,
                        Size = info.Length,
                        LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks,
                        StrongHash = strongHash,
                        Domain = domain
                    };
                }
                catch (Exception exception)
                {
                    Log.Warning($"GAGARIN: Could not fingerprint '{path}' for {result.PackageId}. The file will force revalidation.\n{exception}");
                    var relative = GetRelativePath(root, path);
                    result.Files[relative] = new FileFingerprint
                    {
                        RelativePath = relative,
                        Size = -1,
                        LastWriteUtcTicks = DateTime.UtcNow.Ticks,
                        StrongHash = PipelineHash.TextSha256(exception.GetType().FullName + ":" + exception.Message),
                        Domain = Classify(relative)
                    };
                }
            }

            result.XmlSignature = SignatureFor(result, CacheDomain.Xml);
            result.CodeSignature = SignatureFor(result, CacheDomain.Code);
            result.TextureSignature = SignatureFor(result, CacheDomain.Texture);
            result.MetadataSignature = SignatureFor(result, CacheDomain.Metadata);
            return result;
        }

        private static IEnumerable<string> EnumerateRelevantFiles(string root)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
            }
            catch (Exception exception)
            {
                Log.Warning($"GAGARIN: Could not enumerate files under '{root}'.\n{exception}");
                yield break;
            }

            foreach (var file in files)
            {
                var relative = GetRelativePath(root, file);
                if (Classify(relative) != CacheDomain.None)
                    yield return file;
            }
        }

        private static CacheDomain Classify(string relativePath)
        {
            var normalized = (relativePath ?? string.Empty).Replace('\\', '/');
            var lower = normalized.ToLowerInvariant();
            var fileName = Path.GetFileName(lower);
            var extension = Path.GetExtension(lower);

            if (fileName == "about.xml" || fileName == "loadfolders.xml" ||
                lower.StartsWith("about/", StringComparison.Ordinal))
                return CacheDomain.Metadata;

            if (lower.StartsWith("assemblies/", StringComparison.Ordinal) && extension == ".dll")
                return CacheDomain.Code;

            if ((lower.StartsWith("defs/", StringComparison.Ordinal) ||
                 lower.StartsWith("patches/", StringComparison.Ordinal)) && extension == ".xml")
                return CacheDomain.Xml;

            if (lower.StartsWith("textures/", StringComparison.Ordinal) && TextureExtensions.Contains(extension))
                return CacheDomain.Texture;

            return CacheDomain.None;
        }

        private static string SignatureFor(ModFingerprint mod, CacheDomain domain)
        {
            return PipelineHash.Aggregate(mod.Files.Values
                .Where(file => (file.Domain & domain) != 0)
                .Select(file => file.StableValue));
        }

        private static string GetRelativePath(string root, string path)
        {
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                           + Path.DirectorySeparatorChar;
            var pathFull = Path.GetFullPath(path);
            if (pathFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                return pathFull.Substring(rootFull.Length).Replace('\\', '/');
            return pathFull.Replace('\\', '/');
        }
    }
}
