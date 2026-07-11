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

            foreach (var path in EnumerateRelevantFiles(root, result))
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
                    AddScanFailure(result, root, path, exception);
                }
            }

            result.XmlSignature = SignatureFor(result, CacheDomain.Xml);
            result.CodeSignature = SignatureFor(result, CacheDomain.Code);
            result.TextureSignature = SignatureFor(result, CacheDomain.Texture);
            result.MetadataSignature = SignatureFor(result, CacheDomain.Metadata);
            return result;
        }

        private static IEnumerable<string> EnumerateRelevantFiles(string root, ModFingerprint result)
        {
            var pending = new Stack<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            pending.Push(Path.GetFullPath(root));

            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                var normalizedDirectory = PipelineHash.NormalizePath(directory);
                if (!visited.Add(normalizedDirectory))
                    continue;

                string[] files;
                try
                {
                    files = Directory.GetFiles(directory);
                }
                catch (Exception exception)
                {
                    Log.Warning($"GAGARIN: Could not enumerate files under '{directory}'. The mod will be revalidated on the next load.\n{exception}");
                    AddScanFailure(result, root, directory, exception);
                    files = Array.Empty<string>();
                }

                foreach (var file in files)
                {
                    var relative = GetRelativePath(root, file);
                    if (Classify(relative) != CacheDomain.None)
                        yield return file;
                }

                string[] directories;
                try
                {
                    directories = Directory.GetDirectories(directory);
                }
                catch (Exception exception)
                {
                    Log.Warning($"GAGARIN: Could not enumerate directories under '{directory}'. The mod will be revalidated on the next load.\n{exception}");
                    AddScanFailure(result, root, directory, exception);
                    continue;
                }

                foreach (var child in directories)
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                        {
                            Log.Warning($"GAGARIN: Skipping reparse-point directory while fingerprinting '{child}'.");
                            continue;
                        }

                        pending.Push(child);
                    }
                    catch (Exception exception)
                    {
                        Log.Warning($"GAGARIN: Could not inspect directory '{child}'. The mod will be revalidated on the next load.\n{exception}");
                        AddScanFailure(result, root, child, exception);
                    }
                }
            }
        }

        private static void AddScanFailure(ModFingerprint result, string root, string path, Exception exception)
        {
            var location = GetRelativePath(root, path);
            var marker = "__scan_error__/" + PipelineHash.TextSha256(location);
            var now = DateTime.UtcNow.Ticks;
            result.Files[marker] = new FileFingerprint
            {
                RelativePath = marker,
                Size = -1,
                LastWriteUtcTicks = now,
                StrongHash = PipelineHash.TextSha256(exception.GetType().FullName + ":" + exception.Message + ":" + now),
                Domain = CacheDomain.Xml | CacheDomain.Code | CacheDomain.Texture | CacheDomain.Metadata
            };
        }

        private static CacheDomain Classify(string relativePath)
        {
            var normalized = (relativePath ?? string.Empty).Replace('\\', '/').Trim('/');
            var lower = normalized.ToLowerInvariant();
            var fileName = Path.GetFileName(lower);
            var extension = Path.GetExtension(lower);
            var segments = lower.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            if (fileName == "about.xml" || fileName == "loadfolders.xml" || HasSegment(segments, "about"))
                return CacheDomain.Metadata;

            if (extension == ".dll" && HasSegment(segments, "assemblies"))
                return CacheDomain.Code;

            if (extension == ".xml" && (HasSegment(segments, "defs") || HasSegment(segments, "patches")))
                return CacheDomain.Xml;

            if (TextureExtensions.Contains(extension) && HasSegment(segments, "textures"))
                return CacheDomain.Texture;

            return CacheDomain.None;
        }

        private static bool HasSegment(IEnumerable<string> segments, string expected)
        {
            return segments.Any(segment => string.Equals(segment, expected, StringComparison.OrdinalIgnoreCase));
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
