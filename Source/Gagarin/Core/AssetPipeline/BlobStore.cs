using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using Verse;

namespace Gagarin
{
    internal sealed class BlobIndexEntry
    {
        public string Key;
        public string BlobHash;
        public string SourcePath;
        public string PackageId;
        public string Profile;
        public long Size;
        public long LastAccessUtcTicks;
        public long CreatedUtcTicks;
    }

    internal sealed class BlobStore
    {
        private readonly object sync = new object();
        private readonly Dictionary<string, BlobIndexEntry> entries =
            new Dictionary<string, BlobIndexEntry>(StringComparer.Ordinal);
        private bool loaded;
        private long hits;
        private long misses;
        private long bytesRead;
        private long bytesWritten;

        public bool TryGet(string key, out string blobPath)
        {
            blobPath = null;
            if (string.IsNullOrEmpty(key))
                return false;

            lock (sync)
            {
                EnsureLoaded();
                if (!entries.TryGetValue(key, out var entry))
                {
                    misses++;
                    return false;
                }

                var path = GetBlobPath(entry.BlobHash);
                if (!File.Exists(path) || new FileInfo(path).Length != entry.Size)
                {
                    entries.Remove(key);
                    DeleteBlobIfUnreferenced(entry.BlobHash);
                    misses++;
                    SaveIndex();
                    return false;
                }

                entry.LastAccessUtcTicks = DateTime.UtcNow.Ticks;
                hits++;
                bytesRead += entry.Size;
                blobPath = path;
                return true;
            }
        }

        public bool Store(string key, byte[] payload, string sourcePath, string packageId, string profile)
        {
            if (string.IsNullOrEmpty(key) || payload == null || payload.Length == 0)
                return false;

            lock (sync)
            {
                EnsureLoaded();
                Directory.CreateDirectory(GagarinEnvironmentInfo.BlobsFolderPath);
                var blobHash = PipelineHash.BytesSha256(payload);
                var blobPath = GetBlobPath(blobHash);
                Directory.CreateDirectory(Path.GetDirectoryName(blobPath));

                var wroteBlob = BlobNeedsReplacement(blobPath, blobHash, payload.LongLength);
                if (wroteBlob)
                    AtomicFile.WriteAllBytes(blobPath, payload);

                entries.TryGetValue(key, out var previousEntry);
                var now = DateTime.UtcNow.Ticks;
                entries[key] = new BlobIndexEntry
                {
                    Key = key,
                    BlobHash = blobHash,
                    SourcePath = PipelineHash.NormalizePath(sourcePath),
                    PackageId = packageId ?? string.Empty,
                    Profile = profile ?? string.Empty,
                    Size = payload.LongLength,
                    LastAccessUtcTicks = now,
                    CreatedUtcTicks = previousEntry != null && string.Equals(previousEntry.BlobHash, blobHash, StringComparison.OrdinalIgnoreCase)
                        ? previousEntry.CreatedUtcTicks
                        : now
                };

                if (wroteBlob)
                    bytesWritten += payload.LongLength;

                if (previousEntry != null && !string.Equals(previousEntry.BlobHash, blobHash, StringComparison.OrdinalIgnoreCase))
                    DeleteBlobIfUnreferenced(previousEntry.BlobHash);

                SaveIndex();
                PruneToBudget(GagarinPrefs.TextureCacheMaxMB * 1024L * 1024L);
                return true;
            }
        }

        public void InvalidatePackage(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
                return;

            lock (sync)
            {
                EnsureLoaded();
                foreach (var key in entries.Values
                             .Where(entry => string.Equals(entry.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
                             .Select(entry => entry.Key).ToList())
                    entries.Remove(key);
                DeleteUnreferencedBlobs();
                SaveIndex();
            }
        }

        public void InvalidateSource(string sourcePath)
        {
            var normalized = PipelineHash.NormalizePath(sourcePath);
            if (string.IsNullOrEmpty(normalized))
                return;

            lock (sync)
            {
                EnsureLoaded();
                foreach (var key in entries.Values
                             .Where(entry => string.Equals(entry.SourcePath, normalized, StringComparison.OrdinalIgnoreCase))
                             .Select(entry => entry.Key).ToList())
                    entries.Remove(key);
                DeleteUnreferencedBlobs();
                SaveIndex();
            }
        }

        public void Clear()
        {
            lock (sync)
            {
                entries.Clear();
                loaded = true;
                if (Directory.Exists(GagarinEnvironmentInfo.BlobsFolderPath))
                    Directory.Delete(GagarinEnvironmentInfo.BlobsFolderPath, true);
                Directory.CreateDirectory(GagarinEnvironmentInfo.BlobsFolderPath);
                if (File.Exists(GagarinEnvironmentInfo.BlobIndexFilePath))
                    File.Delete(GagarinEnvironmentInfo.BlobIndexFilePath);
            }
        }

        public void PruneToBudget(long maximumBytes)
        {
            lock (sync)
            {
                EnsureLoaded();
                maximumBytes = Math.Max(64L * 1024L * 1024L, maximumBytes);
                var groups = entries.Values
                    .GroupBy(entry => entry.BlobHash, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new
                    {
                        BlobHash = group.Key,
                        Size = group.Max(entry => entry.Size),
                        LastAccessUtcTicks = group.Max(entry => entry.LastAccessUtcTicks),
                        Keys = group.Select(entry => entry.Key).ToList()
                    })
                    .OrderBy(group => group.LastAccessUtcTicks)
                    .ToList();

                var total = groups.Sum(group => group.Size);
                if (total <= maximumBytes)
                    return;

                foreach (var group in groups)
                {
                    foreach (var key in group.Keys)
                        entries.Remove(key);
                    total -= group.Size;
                    if (total <= maximumBytes)
                        break;
                }

                DeleteUnreferencedBlobs();
                SaveIndex();
            }
        }

        public string GetStatistics()
        {
            lock (sync)
            {
                EnsureLoaded();
                var totalBytes = GetPhysicalTotalBytes();
                var requests = hits + misses;
                var hitRate = requests == 0 ? 0d : hits / (double)requests;
                return string.Join(";", new[]
                {
                    "entries=" + entries.Count,
                    "blobs=" + entries.Values.Select(entry => entry.BlobHash).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    "bytes=" + totalBytes,
                    "hits=" + hits,
                    "misses=" + misses,
                    "hitRate=" + hitRate.ToString("0.0000", CultureInfo.InvariantCulture),
                    "bytesRead=" + bytesRead,
                    "bytesWritten=" + bytesWritten
                });
            }
        }

        public long TotalBytes
        {
            get
            {
                lock (sync)
                {
                    EnsureLoaded();
                    return GetPhysicalTotalBytes();
                }
            }
        }

        private void EnsureLoaded()
        {
            if (loaded)
                return;
            loaded = true;
            entries.Clear();

            if (!File.Exists(GagarinEnvironmentInfo.BlobIndexFilePath))
            {
                DeleteUnreferencedBlobs();
                return;
            }

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
                using (var reader = XmlReader.Create(GagarinEnvironmentInfo.BlobIndexFilePath, settings))
                    document.Load(reader);

                var root = document.DocumentElement;
                if (root == null || root.Name != "TextureBlobIndex")
                    throw new InvalidDataException("Texture blob index has an invalid root element.");

                foreach (XmlNode node in root.ChildNodes)
                {
                    if (!(node is XmlElement element) || element.Name != "Entry")
                        continue;
                    var entry = new BlobIndexEntry
                    {
                        Key = element.GetAttribute("key"),
                        BlobHash = element.GetAttribute("blob"),
                        SourcePath = element.GetAttribute("source"),
                        PackageId = element.GetAttribute("packageId"),
                        Profile = element.GetAttribute("profile"),
                        Size = ParseLong(element.GetAttribute("size")),
                        LastAccessUtcTicks = ParseLong(element.GetAttribute("lastAccess")),
                        CreatedUtcTicks = ParseLong(element.GetAttribute("created"))
                    };
                    if (!string.IsNullOrEmpty(entry.Key) && IsSha256(entry.BlobHash) && entry.Size > 0)
                        entries[entry.Key] = entry;
                }

                DeleteUnreferencedBlobs();
            }
            catch (Exception exception)
            {
                Log.Warning($"GAGARIN: Texture blob index is invalid and will be rebuilt.\n{exception}");
                entries.Clear();
                try
                {
                    File.Move(GagarinEnvironmentInfo.BlobIndexFilePath,
                        GagarinEnvironmentInfo.BlobIndexFilePath + ".broken-" + DateTime.UtcNow.Ticks);
                }
                catch
                {
                    // The broken index is allowed to remain; the in-memory index is still clean.
                }

                DeleteUnreferencedBlobs();
            }
        }

        private void SaveIndex()
        {
            var document = new XmlDocument();
            var root = document.CreateElement("TextureBlobIndex");
            root.SetAttribute("schema", "2");
            document.AppendChild(root);

            foreach (var entry in entries.Values.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                var node = document.CreateElement("Entry");
                node.SetAttribute("key", entry.Key ?? string.Empty);
                node.SetAttribute("blob", entry.BlobHash ?? string.Empty);
                node.SetAttribute("source", entry.SourcePath ?? string.Empty);
                node.SetAttribute("packageId", entry.PackageId ?? string.Empty);
                node.SetAttribute("profile", entry.Profile ?? string.Empty);
                node.SetAttribute("size", entry.Size.ToString(CultureInfo.InvariantCulture));
                node.SetAttribute("lastAccess", entry.LastAccessUtcTicks.ToString(CultureInfo.InvariantCulture));
                node.SetAttribute("created", entry.CreatedUtcTicks.ToString(CultureInfo.InvariantCulture));
                root.AppendChild(node);
            }

            AtomicFile.SaveXml(GagarinEnvironmentInfo.BlobIndexFilePath, document);
        }

        private void DeleteUnreferencedBlobs()
        {
            if (!Directory.Exists(GagarinEnvironmentInfo.BlobsFolderPath))
                return;

            var referenced = new HashSet<string>(entries.Values.Select(entry => entry.BlobHash), StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> paths;
            try
            {
                paths = Directory.GetFiles(GagarinEnvironmentInfo.BlobsFolderPath, "*.blob", SearchOption.AllDirectories);
            }
            catch (Exception exception)
            {
                Log.Warning($"GAGARIN: Could not enumerate texture blobs for cleanup.\n{exception}");
                return;
            }

            foreach (var path in paths)
            {
                var hash = Path.GetFileNameWithoutExtension(path);
                if (!referenced.Contains(hash))
                    DeleteBlob(path);
            }
        }

        private void DeleteBlobIfUnreferenced(string blobHash)
        {
            if (string.IsNullOrEmpty(blobHash) || entries.Values.Any(entry => string.Equals(entry.BlobHash, blobHash, StringComparison.OrdinalIgnoreCase)))
                return;
            DeleteBlob(GetBlobPath(blobHash));
        }

        private static void DeleteBlob(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception exception)
            {
                Log.Warning($"GAGARIN: Could not delete unused texture blob '{path}'.\n{exception}");
            }
        }

        private static bool BlobNeedsReplacement(string path, string expectedHash, long expectedSize)
        {
            if (!File.Exists(path))
                return true;

            try
            {
                var info = new FileInfo(path);
                if (info.Length != expectedSize)
                    return true;
                return !string.Equals(PipelineHash.FileSha256(path), expectedHash, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }

        private long GetPhysicalTotalBytes()
        {
            return entries.Values
                .GroupBy(entry => entry.BlobHash, StringComparer.OrdinalIgnoreCase)
                .Sum(group => group.Max(entry => entry.Size));
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64)
                return false;
            foreach (var character in value)
            {
                var isDigit = character >= '0' && character <= '9';
                var isLowerHex = character >= 'a' && character <= 'f';
                var isUpperHex = character >= 'A' && character <= 'F';
                if (!isDigit && !isLowerHex && !isUpperHex)
                    return false;
            }
            return true;
        }

        private static string GetBlobPath(string hash)
        {
            var prefix = string.IsNullOrEmpty(hash) || hash.Length < 2 ? "00" : hash.Substring(0, 2);
            return Path.Combine(GagarinEnvironmentInfo.BlobsFolderPath, prefix, (hash ?? "missing") + ".blob");
        }

        private static long ParseLong(string value)
        {
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0L;
        }
    }
}
