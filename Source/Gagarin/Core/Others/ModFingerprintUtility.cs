using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Verse;

namespace Gagarin
{
    internal static class ModFingerprintUtility
    {
        private static readonly HashSet<string> RelevantExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".dll", ".xml", ".png", ".dds", ".jpg", ".jpeg"
        };

        public static bool Changed(List<ModContentPack> mods, string path)
        {
            if (!File.Exists(path))
                return true;

            Dictionary<string, string> previous = Load(path);
            Dictionary<string, string> current = Build(mods);
            if (previous.Count != current.Count)
                return true;

            foreach (KeyValuePair<string, string> pair in current)
            {
                if (!previous.TryGetValue(pair.Key, out string oldFingerprint)
                    || !string.Equals(pair.Value, oldFingerprint, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        public static void Dump(List<ModContentPack> mods, string path)
        {
            Dictionary<string, string> fingerprints = Build(mods);
            AtomicFile.Write(path, temporaryPath =>
            {
                XmlDocument document = new XmlDocument();
                XmlElement root = document.CreateElement("ModFingerprints");
                document.AppendChild(root);

                foreach (KeyValuePair<string, string> pair in fingerprints)
                {
                    XmlElement element = document.CreateElement("Mod");
                    element.SetAttribute("key", pair.Key);
                    element.SetAttribute("fingerprint", pair.Value);
                    root.AppendChild(element);
                }

                XmlWriterSettings settings = new XmlWriterSettings
                {
                    CheckCharacters = false,
                    Indent = true,
                    NewLineChars = "\n"
                };
                using XmlWriter writer = XmlWriter.Create(temporaryPath, settings);
                document.Save(writer);
            });
        }

        private static Dictionary<string, string> Build(List<ModContentPack> mods)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            for (int index = 0; index < mods.Count; index++)
            {
                ModContentPack mod = mods[index];
                string key = $"{index}:{mod.PackageId}";
                result[key] = BuildFingerprint(mod);
            }
            return result;
        }

        private static Dictionary<string, string> Load(string path)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(path);
                if (document.DocumentElement == null)
                    return result;

                foreach (XmlNode node in document.DocumentElement.ChildNodes)
                {
                    if (node is not XmlElement element || element.Name != "Mod")
                        continue;
                    result[element.GetAttribute("key")] = element.GetAttribute("fingerprint");
                }
            }
            catch (Exception exception)
            {
                Log.Warning($"GAGARIN: Failed loading mod fingerprint manifest: {exception}");
                return new Dictionary<string, string>();
            }
            return result;
        }

        private static string BuildFingerprint(ModContentPack mod)
        {
            string rootPath = GetRootDirectory(mod);
            StringBuilder builder = new StringBuilder();
            builder.Append(mod.PackageId).Append('|').Append(NormalizePath(rootPath));

            if (!Directory.Exists(rootPath))
                return HashText(builder.ToString());

            try
            {
                IEnumerable<string> files = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
                    .Where(IsRelevantFile)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

                foreach (string file in files)
                {
                    FileInfo info = new FileInfo(file);
                    string relativePath = GetRelativePath(rootPath, file);
                    builder.Append('\n').Append(relativePath)
                        .Append('|').Append(info.Length)
                        .Append('|').Append(info.LastWriteTimeUtc.Ticks);

                    if (ShouldStrongHash(file))
                        builder.Append('|').Append(HashFile(file));
                }
            }
            catch (Exception exception)
            {
                builder.Append("|enumeration-error|").Append(exception.GetType().FullName);
            }

            return HashText(builder.ToString());
        }

        private static string GetRootDirectory(ModContentPack mod)
        {
            PropertyInfo property = mod.GetType().GetProperty("RootDir",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property?.GetValue(mod) as string ?? string.Empty;
        }

        private static bool IsRelevantFile(string path)
        {
            string fileName = Path.GetFileName(path);
            if (fileName.Equals("About.xml", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("LoadFolders.xml", StringComparison.OrdinalIgnoreCase))
                return true;
            return RelevantExtensions.Contains(Path.GetExtension(path));
        }

        private static bool ShouldStrongHash(string path)
        {
            string extension = Path.GetExtension(path);
            string fileName = Path.GetFileName(path);
            return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                   || fileName.Equals("About.xml", StringComparison.OrdinalIgnoreCase)
                   || fileName.Equals("LoadFolders.xml", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRelativePath(string rootPath, string fullPath)
        {
            string normalizedRoot = NormalizePath(rootPath).TrimEnd('/') + "/";
            string normalizedFull = NormalizePath(fullPath);
            return normalizedFull.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                ? normalizedFull.Substring(normalizedRoot.Length)
                : normalizedFull;
        }

        private static string NormalizePath(string path) => (path ?? string.Empty).Replace('\\', '/');

        private static string HashText(string value)
        {
            using SHA256 sha = SHA256.Create();
            return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty)));
        }

        private static string HashFile(string path)
        {
            using SHA256 sha = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return ToHex(sha.ComputeHash(stream));
        }

        private static string ToHex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty);
    }
}
