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
    public static class AssetHashingUtility
    {
        public static Dictionary<string, string> Load(string path)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            try
            {
                XmlDocument document = LoadDocument(path);
                if (document.DocumentElement == null)
                    return result;

                foreach (XmlNode node in document.DocumentElement.ChildNodes)
                {
                    if (node is not XmlElement element)
                        continue;
                    result[element.GetAttribute("id")] = element.GetAttribute("hash");
                }
            }
            catch (Exception exception)
            {
                Log.Error($"GAGARIN: Error while loading the old hashes dump. Deleting it. {exception}");
                if (File.Exists(path))
                    File.Delete(path);
            }
            return result;
        }

        public static Dictionary<string, ulong> LoadInt(string path)
        {
            Dictionary<string, ulong> result = new Dictionary<string, ulong>();
            try
            {
                XmlDocument document = LoadDocument(path);
                if (document.DocumentElement == null)
                    return result;

                foreach (XmlNode node in document.DocumentElement.ChildNodes)
                {
                    if (node is not XmlElement element)
                        continue;
                    if (ulong.TryParse(element.GetAttribute("hash"), out ulong hash))
                        result[element.GetAttribute("id")] = hash;
                }
            }
            catch (Exception exception)
            {
                Log.Error($"GAGARIN: Error while loading the old integer hashes dump. Deleting it. {exception}");
                if (File.Exists(path))
                    File.Delete(path);
            }
            return result;
        }

        public static void Dump<T>(Dictionary<string, T> hashes, string path)
        {
            AtomicFile.Write(path, temporaryPath =>
            {
                XmlDocument document = new XmlDocument { XmlResolver = null };
                XmlElement root = document.CreateElement("AssetsHash");
                document.AppendChild(root);

                foreach (KeyValuePair<string, T> assetHashPair in hashes.OrderBy(pair => pair.Key,
                             StringComparer.Ordinal))
                {
                    XmlElement assetXml = document.CreateElement("Asset");
                    assetXml.SetAttribute("id", assetHashPair.Key);
                    assetXml.SetAttribute("hash", $"{assetHashPair.Value}");
                    root.AppendChild(assetXml);
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

        public static string CalculateHashMd5(string text)
        {
            using MD5 md5Hasher = MD5.Create();
            byte[] data = md5Hasher.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
            return BitConverter.ToString(data);
        }

        public static ulong CalculateHash(string read, bool lowTolerance = true)
        {
            ulong hashedValue = 0;
            int i = 0;
            ulong multiplier = 1193;
            while (i < read.Length)
            {
                hashedValue += read[i] * multiplier;
                multiplier *= 37;
                i += lowTolerance ? 2 : 1;
            }
            return hashedValue;
        }

        public static void CalculateFileHashes(string path, out string md5Hash, out ulong contentHash)
        {
            const int bufferSize = 64 * 1024;
            byte[] buffer = new byte[bufferSize];
            ulong fnv = 1469598103934665603UL;

            using MD5 md5 = MD5.Create();
            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize, FileOptions.SequentialScan);

            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                md5.TransformBlock(buffer, 0, read, null, 0);
                for (int i = 0; i < read; i++)
                {
                    fnv ^= buffer[i];
                    fnv *= 1099511628211UL;
                }
            }

            md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            md5Hash = BitConverter.ToString(md5.Hash ?? Array.Empty<byte>());
            contentHash = fnv;
        }

        private static XmlDocument LoadDocument(string path)
        {
            XmlReaderSettings settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true,
                CheckCharacters = false,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            XmlDocument document = new XmlDocument { XmlResolver = null };
            using FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using XmlReader xmlReader = XmlReader.Create(input, settings);
            document.Load(xmlReader);
            return document;
        }
    }
}
