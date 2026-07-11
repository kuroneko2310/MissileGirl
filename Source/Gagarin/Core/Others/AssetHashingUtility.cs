using System;
using System.Collections.Generic;
using System.IO;
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
            XmlDocument document = new XmlDocument();
            XmlReaderSettings settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true,
                CheckCharacters = false
            };
            try
            {
                using StringReader input = new StringReader(File.ReadAllText(path));
                using XmlReader xmlReader = XmlReader.Create(input, settings);
                document.Load(xmlReader);
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
            XmlDocument document = new XmlDocument();
            XmlReaderSettings settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true,
                CheckCharacters = false
            };
            try
            {
                using StringReader input = new StringReader(File.ReadAllText(path));
                using XmlReader xmlReader = XmlReader.Create(input, settings);
                document.Load(xmlReader);
                if (document.DocumentElement == null)
                    return result;

                foreach (XmlNode node in document.DocumentElement.ChildNodes)
                {
                    if (node is not XmlElement element)
                        continue;
                    result[element.GetAttribute("id")] = ulong.Parse(element.GetAttribute("hash"));
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
                XmlDocument document = new XmlDocument();
                XmlElement root = document.CreateElement("AssetsHash");
                document.AppendChild(root);

                foreach (KeyValuePair<string, T> assetHashPair in hashes)
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
    }
}
