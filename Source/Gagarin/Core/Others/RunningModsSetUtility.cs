using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Verse;

namespace Gagarin
{
    public static class RunningModsSetUtility
    {
        public static List<string> Load(string path)
        {
            List<string> result = new List<string>();
            XmlDocument document = new XmlDocument();
            try
            {
                XmlReaderSettings settings = new XmlReaderSettings
                {
                    IgnoreComments = true,
                    IgnoreWhitespace = true,
                    CheckCharacters = false
                };
                using StringReader input = new StringReader(File.ReadAllText(path));
                using XmlReader xmlReader = XmlReader.Create(input, settings);
                document.Load(xmlReader);
                if (document.DocumentElement == null)
                    return result;

                foreach (XmlNode node in document.DocumentElement.ChildNodes)
                {
                    if (node is not XmlElement modXml || modXml.Name != "Mod")
                        continue;
                    result.Add(modXml.GetAttribute("packageId"));
                }
            }
            catch (Exception exception)
            {
                Log.Error($"GAGARIN: Error while loading the old mod list dump. Deleting it. {exception}");
                if (File.Exists(path))
                    File.Delete(path);
            }
            return result;
        }

        public static void Dump(List<ModContentPack> mods, string path)
        {
            AtomicFile.Write(path, temporaryPath =>
            {
                XmlDocument document = new XmlDocument();
                XmlElement root = document.CreateElement("RunningMods");
                document.AppendChild(root);

                foreach (ModContentPack mod in mods)
                {
                    XmlElement modXml = document.CreateElement("Mod");
                    modXml.SetAttribute("packageId", mod.PackageId);
                    root.AppendChild(modXml);
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

        public static bool Changed(List<string> current, string path)
        {
            if (!File.Exists(path))
                return true;

            List<string> old = Load(path);
            if (current.Count != old.Count)
                return true;

            for (int i = 0; i < current.Count; i++)
            {
                if (!string.Equals(current[i], old[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
