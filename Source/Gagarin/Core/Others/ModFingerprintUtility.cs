using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Verse;

namespace Gagarin
{
    internal enum ModFingerprintDomain
    {
        Xml,
        Textures
    }

    internal static class ModFingerprintUtility
    {
        private static readonly HashSet<string> TextureExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".dds", ".jpg", ".jpeg", ".tga", ".bmp"
        };

        public static bool Changed(List<ModContentPack> mods, string path, ModFingerprintDomain domain)
        {
            if (!File.Exists(path))
                return true;

            Dictionary<string, string> previous = Load(path);
            Dictionary<string, string> current = Build(mods, domain);
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

        public static void Dump(List<ModContentPack> mods, string path, ModFingerprintDomain domain)
        {
            Dictionary<string, string> fingerprints = Build(mods, domain);
            AtomicFile.Write(path, temporaryPath =>
            {
                XmlDocument document = new XmlDocument();
                XmlElement root = document.CreateElement("ModFingerprints");
                root.SetAttribute("domain", domain.ToString());
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

        private static Dictionary<string, string> Build(List<ModContentPack> mods, ModFingerprintDomain domain)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            for (int index = 0; index < mods.Count; index++)
            {
                ModContentPack mod = mods[index];
                string key = $"{index}:{mod.PackageId}";
                result[key] = BuildFingerprint(mod, domain);
            }
            return result;
        }

        private static Dictionary<string, string> Load(string path)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            try
            {
                XmlDocument document = new XmlDocument { XmlResolver = null };
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
                Log.Warning($"GAGARIN: Failed loading mod fingerprint manifest '{path}': {exception}");
                return new Dictionary<string, string>();
            }
            return result;
        }

        private static string BuildFingerprint(ModContentPack mod, ModFingerprintDomain domain)
        {
            string rootPath = mod.RootDir ?? string.Empty;
            StringBuilder builder = new StringBuilder();
            builder.Append("domain=").Append(domain)
                .Append("|package=").Append(mod.PackageId)
                .Append("|root=").Append(NormalizePath(rootPath));

            AddMetadataFile(builder, Path.Combine(rootPath, "About", "About.xml"), true);
            AddMetadataFile(builder, Path.Combine(rootPath, "LoadFolders.xml"), true);

            HashSet<string> seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> loadFolders = mod.foldersToLoadDescendingOrder ?? new List<string> { rootPath };
            for (int folderIndex = 0; folderIndex < loadFolders.Count; folderIndex++)
            {
                string loadFolder = loadFolders[folderIndex];
                builder.Append("\nloadFolder[").Append(folderIndex).Append("]=")
                    .Append(NormalizePath(loadFolder));

                if (domain == ModFingerprintDomain.Xml)
                {
                    // XML contents are hashed by LoadableXmlAsset_Patch later in the same startup.
                    // Metadata here gives an early rejection without reading every XML file twice.
                    AddFolder(builder, Path.Combine(loadFolder, "Defs"), seenFiles, IsXmlInput, false);
                    AddFolder(builder, Path.Combine(loadFolder, "Patches"), seenFiles, IsXmlInput, false);
                    AddFolder(builder, Path.Combine(loadFolder, "Assemblies"), seenFiles, IsAssembly, true);
                }
                else
                {
                    AddFolder(builder, Path.Combine(loadFolder, "Textures"), seenFiles, IsTextureInput, false);
                    AddFolder(builder, Path.Combine(loadFolder, "Assemblies"), seenFiles, IsAssembly, true);
                }
            }

            return HashText(builder.ToString());
        }

        private static void AddFolder(StringBuilder builder, string folder, HashSet<string> seenFiles,
            Func<string, bool> predicate, bool strongHash)
        {
            if (!Directory.Exists(folder))
                return;

            try
            {
                foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                             .Where(predicate)
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    string fullPath = Path.GetFullPath(file);
                    if (!seenFiles.Add(fullPath))
                        continue;
                    AddMetadataFile(builder, fullPath, strongHash);
                }
            }
            catch (Exception exception)
            {
                builder.Append("\nfolder-error=").Append(NormalizePath(folder))
                    .Append('|').Append(exception.GetType().FullName);
            }
        }

        private static void AddMetadataFile(StringBuilder builder, string path, bool strongHash)
        {
            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists)
                {
                    builder.Append("\nmissing=").Append(NormalizePath(path));
                    return;
                }

                builder.Append("\nfile=").Append(NormalizePath(info.FullName))
                    .Append('|').Append(info.Length)
                    .Append('|').Append(info.LastWriteTimeUtc.Ticks);

                if (strongHash)
                    builder.Append('|').Append(HashFile(info.FullName));
            }
            catch (Exception exception)
            {
                builder.Append("\nfile-error=").Append(NormalizePath(path))
                    .Append('|').Append(exception.GetType().FullName);
            }
        }

        private static bool IsXmlInput(string path) =>
            Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase);

        private static bool IsAssembly(string path) =>
            Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase);

        private static bool IsTextureInput(string path) => TextureExtensions.Contains(Path.GetExtension(path));

        private static string NormalizePath(string path) => (path ?? string.Empty).Replace('\\', '/');

        private static string HashText(string value)
        {
            using SHA256 sha = SHA256.Create();
            return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty)));
        }

        private static string HashFile(string path)
        {
            using SHA256 sha = SHA256.Create();
            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return ToHex(sha.ComputeHash(stream));
        }

        private static string ToHex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty);
    }
}
