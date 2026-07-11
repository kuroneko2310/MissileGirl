// // Copyright (c) 2026 ViralReaction
// //
// // This program and the accompanying materials are made available under the
// // terms of the Eclipse Public License 2.0 which is available at
// // http://www.eclipse.org/legal/epl-2.0.
// //
// // SPDX-License-Identifier: EPL-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml;
using MissileGirl;
using Verse;
using static Verse.XmlInheritance;

namespace Gagarin
{
    public static class CachedDefHelper
    {
        private static XmlDocument document;
        private static readonly List<DefXmlUnit> defs = new List<DefXmlUnit>();
        private static readonly HashSet<string> registeredNames = new HashSet<string>();

        private class DefXmlUnit
        {
            public Def def;
            public XmlNode node;
            public LoadableXmlAsset asset;
            public XmlInheritanceNode inheritanceNode;
        }

        private sealed class CachedXmlEntry
        {
            public XmlNode Node;
            public string Path;
        }

        public static void Prepare()
        {
            if (Context.IsUsingCache)
                return;

            document = new XmlDocument();
            document.AppendChild(document.CreateElement("DefXmlStorage"));
        }

        public static void Clean()
        {
            defs.Clear();
            registeredNames.Clear();
            document?.RemoveAll();
            document = null;
        }

        public static void Register(Def def, XmlNode node, LoadableXmlAsset asset)
        {
            defs.Add(new DefXmlUnit
            {
                def = def,
                node = node,
                asset = asset,
                inheritanceNode = XmlInheritance.resolvedNodes.TryGetValue(node, out XmlInheritanceNode inheritanceNode)
                    ? inheritanceNode
                    : null
            });
        }

        public static void Save()
        {
            if (document?.DocumentElement == null)
                throw new InvalidOperationException("GAGARIN: XML cache document was not prepared before Save().");

            var stopwatch = Stopwatch.StartNew();
            var root = document.DocumentElement;

            foreach (var unit in defs)
            {
                if (!(unit.node is XmlElement sourceNode))
                    continue;

                XmlElement cachedNode;
                if (unit.inheritanceNode == null)
                {
                    cachedNode = sourceNode;
                }
                else
                {
                    if (!(unit.inheritanceNode.resolvedXmlNode is XmlElement resolvedSource))
                    {
                        Log.Error($"GAGARIN: {unit.def?.defName ?? "Unknown Def"} has <color=yellow>resolvedXmlNode == null!</color>");
                        continue;
                    }

                    // Never mutate Verse's inheritance-resolved node. Cache serialization works on a deep clone.
                    var resolvedClone = (XmlElement)document.ImportNode(resolvedSource, true);
                    resolvedClone.RemoveAttribute("ParentName");

                    if (resolvedClone.Name != sourceNode.Name)
                    {
                        var renamedNode = document.CreateElement(sourceNode.Name);
                        foreach (XmlNode child in resolvedClone.ChildNodes)
                        {
                            if (child.NodeType == XmlNodeType.Element)
                                renamedNode.AppendChild(document.ImportNode(child, true));
                        }

                        cachedNode = renamedNode;
                    }
                    else
                    {
                        if (sourceNode.HasAttribute("Class") && !resolvedClone.HasAttribute("Class"))
                            resolvedClone.SetAttribute("Class", sourceNode.GetAttribute("Class"));

                        cachedNode = resolvedClone;
                    }
                }

                var wrapper = WrapXmlNode(cachedNode, unit.asset?.FullFilePath);
                if (unit.inheritanceNode != null)
                    wrapper.SetAttribute("resolved", "true");

                root.AppendChild(wrapper);
            }

            var settings = new XmlWriterSettings
            {
                CheckCharacters = false,
                Indent = true,
                NewLineChars = "\n"
            };
            SaveXmlAtomically(document, GagarinEnvironmentInfo.UnifiedXmlFilePath, settings);

            stopwatch.Stop();
            Log.Warning($"GAGARIN: <color=white>Cache created!</color> creating cache took <color=green>{stopwatch.ElapsedMilliseconds / 1000} seconds</color>");
        }

        public static void Load(XmlDocument targetDocument, Dictionary<XmlNode, LoadableXmlAsset> assets)
        {
            if (targetDocument == null)
                throw new ArgumentNullException(nameof(targetDocument));
            if (assets == null)
                throw new ArgumentNullException(nameof(assets));

            var stopwatch = Stopwatch.StartNew();
            var settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true,
                CheckCharacters = false,
                DtdProcessing = DtdProcessing.Prohibit
            };

            var unifiedDocument = new XmlDocument();
            var documentStopwatch = Stopwatch.StartNew();
            using (var xmlReader = XmlReader.Create(GagarinEnvironmentInfo.UnifiedXmlFilePath, settings))
                unifiedDocument.Load(xmlReader);
            documentStopwatch.Stop();

            if (unifiedDocument.DocumentElement == null || unifiedDocument.DocumentElement.Name != "DefXmlStorage")
                throw new InvalidDataException("GAGARIN: Unified.xml has an invalid or missing DefXmlStorage root element.");

            if (Prefs.LogVerbose)
            {
                Log.Warning($"GAGARIN: <color=green>Loading XmlDocument</color> took <color=red>{
                    (float)documentStopwatch.ElapsedTicks / Stopwatch.Frequency} seconds</color>");
            }

            var entries = new List<CachedXmlEntry>(unifiedDocument.DocumentElement.ChildNodes.Count);
            foreach (XmlNode child in unifiedDocument.DocumentElement.ChildNodes)
            {
                if (!(child is XmlElement wrapper))
                    continue;

                var cachedNode = wrapper.FirstChild;
                if (cachedNode == null || cachedNode.NodeType != XmlNodeType.Element)
                    throw new InvalidDataException("GAGARIN: Unified.xml contains a cache item without a Def element.");

                entries.Add(new CachedXmlEntry
                {
                    Node = cachedNode,
                    Path = wrapper.GetAttribute("path")
                });
            }

            targetDocument.RemoveAll();
            targetDocument.AppendChild(targetDocument.CreateElement("Defs"));
            assets.Clear();

            foreach (var entry in entries)
            {
                var defXml = targetDocument.ImportNode(entry.Node, true);
                targetDocument.DocumentElement.AppendChild(defXml);

                if (!entry.Path.NullOrEmpty() && Context.XmlAssets.TryGetValue(entry.Path, out var asset))
                    assets[defXml] = asset;
            }

            stopwatch.Stop();
            if (Prefs.LogVerbose)
            {
                Log.Warning($"GAGARIN: <color=green>Loaded from cache!</color> Loading cache took <color=red>{stopwatch.ElapsedMilliseconds / 1000} seconds</color>");
            }
            else
            {
                Log.Warning("GAGARIN: <color=green>Finished loading XML from cache!</color>");
            }
        }

        private static XmlElement WrapXmlNode(XmlNode node, string path = null)
        {
            if (node == null)
                throw new ArgumentNullException(nameof(node));

            var wrapper = document.CreateElement("Item");
            wrapper.SetAttribute("path", path ?? string.Empty);
            wrapper.AppendChild(document.ImportNode(node, true));
            return wrapper;
        }

        private static void SaveXmlAtomically(XmlDocument sourceDocument, string destinationPath, XmlWriterSettings settings)
        {
            var temporaryPath = destinationPath + ".tmp";
            var backupPath = destinationPath + ".bak";

            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);

                using (var writer = XmlWriter.Create(temporaryPath, settings))
                    sourceDocument.Save(writer);

                // Re-open the temporary file before activation so a truncated/invalid document is never promoted.
                var verificationDocument = new XmlDocument();
                verificationDocument.Load(temporaryPath);
                if (verificationDocument.DocumentElement == null)
                    throw new InvalidDataException("GAGARIN: Refusing to activate an XML cache without a root element.");

                if (File.Exists(destinationPath))
                {
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);

                    File.Replace(temporaryPath, destinationPath, backupPath, true);

                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                }
                else
                {
                    File.Move(temporaryPath, destinationPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
    }
}
