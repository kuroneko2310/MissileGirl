using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml;
using Verse;
using static Verse.XmlInheritance;

namespace Gagarin
{
    public static class CachedDefHelper
    {
        private static XmlDocument document;
        private static readonly List<DefXmlUnit> Defs = new List<DefXmlUnit>();

        private class DefXmlUnit
        {
            public Def def;
            public XmlNode node;
            public LoadableXmlAsset asset;
            public XmlInheritanceNode inheritanceNode;
        }

        public static void Prepare()
        {
            if (Context.IsUsingCache)
                return;

            document = new XmlDocument();
            document.AppendChild(document.CreateElement("DefXmlStorage"));
            Defs.Clear();
        }

        public static void Clean()
        {
            Defs.Clear();
            document?.RemoveAll();
            document = null;
        }

        public static void Register(Def def, XmlNode node, LoadableXmlAsset asset)
        {
            if (document == null || def == null || node == null)
                return;

            Defs.Add(new DefXmlUnit
            {
                def = def,
                node = node,
                asset = asset,
                inheritanceNode = XmlInheritance.resolvedNodes.TryGetValue(node,
                    out XmlInheritanceNode inheritanceNode)
                    ? inheritanceNode
                    : null
            });
        }

        public static void Save()
        {
            if (document?.DocumentElement == null)
                throw new InvalidOperationException("Gagarin cache document was not prepared");

            Stopwatch stopwatch = Stopwatch.StartNew();
            XmlElement root = document.DocumentElement;

            foreach (DefXmlUnit unit in Defs)
            {
                if (unit.node is not XmlElement sourceNode)
                    continue;

                XmlElement cacheNode;
                bool resolved = unit.inheritanceNode != null;
                if (!resolved)
                {
                    cacheNode = sourceNode;
                }
                else
                {
                    if (unit.inheritanceNode.resolvedXmlNode is not XmlElement resolvedSource)
                    {
                        Log.Error($"GAGARIN: {unit.def.defName} has resolvedXmlNode == null or non-element");
                        continue;
                    }

                    XmlElement resolvedClone = (XmlElement)document.ImportNode(resolvedSource, true);
                    resolvedClone.RemoveAttribute("ParentName");

                    if (resolvedClone.Name != sourceNode.Name)
                    {
                        XmlElement renamed = document.CreateElement(sourceNode.Name);
                        foreach (XmlAttribute attribute in resolvedClone.Attributes)
                        {
                            if (!attribute.Name.Equals("ParentName", StringComparison.OrdinalIgnoreCase))
                                renamed.SetAttribute(attribute.Name, attribute.Value);
                        }

                        foreach (XmlNode child in resolvedClone.ChildNodes)
                            renamed.AppendChild(document.ImportNode(child, true));

                        resolvedClone = renamed;
                    }

                    if (sourceNode.HasAttribute("Class") && !resolvedClone.HasAttribute("Class"))
                        resolvedClone.SetAttribute("Class", sourceNode.GetAttribute("Class"));

                    cacheNode = resolvedClone;
                }

                XmlElement wrapper = WrapXmlNode(cacheNode, unit.asset?.FullFilePath);
                if (resolved)
                    wrapper.SetAttribute("resolved", "true");
                root.AppendChild(wrapper);
            }

            AtomicFile.Write(GagarinEnvironmentInfo.UnifiedXmlFilePath, temporaryPath =>
            {
                XmlWriterSettings settings = new XmlWriterSettings
                {
                    CheckCharacters = false,
                    Indent = true,
                    NewLineChars = "\n"
                };
                using XmlWriter writer = XmlWriter.Create(temporaryPath, settings);
                document.Save(writer);
            });
            CacheIntegrityUtility.WriteSha256(GagarinEnvironmentInfo.UnifiedXmlFilePath,
                GagarinEnvironmentInfo.UnifiedXmlHashFilePath);

            stopwatch.Stop();
            Log.Warning($"GAGARIN: <color=white>Cache created!</color> Creating cache took <color=green>{stopwatch.Elapsed.TotalSeconds:F2} seconds</color>");
        }

        public static void Load(XmlDocument targetDocument, Dictionary<XmlNode, LoadableXmlAsset> assets)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            if (!CacheIntegrityUtility.ValidateSha256(GagarinEnvironmentInfo.UnifiedXmlFilePath,
                    GagarinEnvironmentInfo.UnifiedXmlHashFilePath))
                throw new InvalidDataException("Unified XML cache checksum validation failed");

            XmlReaderSettings settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true,
                CheckCharacters = false,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            XmlDocument unifiedDocument = new XmlDocument { XmlResolver = null };
            using (FileStream input = new FileStream(GagarinEnvironmentInfo.UnifiedXmlFilePath,
                       FileMode.Open, FileAccess.Read, FileShare.Read))
            using (XmlReader xmlReader = XmlReader.Create(input, settings))
                unifiedDocument.Load(xmlReader);

            if (unifiedDocument.DocumentElement == null || unifiedDocument.DocumentElement.Name != "DefXmlStorage")
                throw new InvalidDataException("Unified XML cache has an invalid root element");

            assets.Clear();
            targetDocument.RemoveAll();
            targetDocument.AppendChild(targetDocument.CreateElement("Defs"));

            foreach (XmlNode child in unifiedDocument.DocumentElement.ChildNodes)
            {
                if (child is not XmlElement element)
                    continue;

                XmlElement cachedDef = null;
                foreach (XmlNode wrapperChild in element.ChildNodes)
                {
                    if (wrapperChild is XmlElement defElement)
                    {
                        cachedDef = defElement;
                        break;
                    }
                }

                if (cachedDef == null)
                    continue;

                XmlNode defXml = targetDocument.ImportNode(cachedDef, true);
                string path = element.GetAttribute("path");
                if (Context.XmlAssets.TryGetValue(path, out LoadableXmlAsset asset))
                    assets[defXml] = asset;

                targetDocument.DocumentElement.AppendChild(defXml);
            }

            stopwatch.Stop();
            Log.Warning(Prefs.LogVerbose
                ? $"GAGARIN: <color=green>Loaded XML cache</color> in <color=red>{stopwatch.Elapsed.TotalSeconds:F2} seconds</color>"
                : "GAGARIN: <color=green>Finished loading XML from cache!</color>");
        }

        private static XmlElement WrapXmlNode(XmlNode node, string path = null)
        {
            XmlElement wrapper = document.CreateElement("Item");
            wrapper.SetAttribute("path", path ?? string.Empty);
            wrapper.AppendChild(document.ImportNode(node, true));
            return wrapper;
        }
    }
}
