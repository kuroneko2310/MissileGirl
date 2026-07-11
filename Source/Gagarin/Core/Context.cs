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
using System.Text;
using System.Xml;
using Verse;

namespace Gagarin
{
    public static class Context
    {
        private static bool isUsingCache;

        public static bool IsUsingCache
        {
            get => isUsingCache;
            set
            {
                if (!value && value != isUsingCache)
                {
                    var trace = new StackTrace(1);
                    var builder = new StringBuilder("GAGARIN: <color=yellow>Cache disabled from</color>");
                    for (var index = 0; index < trace.FrameCount; index++)
                    {
                        var frame = trace.GetFrame(index);
                        builder.AppendInNewLine($"{frame.GetMethod().DeclaringType?.FullName}:{frame.GetMethod().Name}():{frame.GetFileLineNumber()}");
                    }
                    Log.Warning(builder.ToString());
                    MissileGirl.Logger.Debug(builder.ToString());
                }

                // Invalidating a cache is a state transition, not a destructive delete. GenerationStore
                // preserves the last READY generation until a replacement has been committed.
                isUsingCache = value;
            }
        }

        private static bool isLoadingModXml;
        public static bool IsLoadingModXML
        {
            get => isLoadingModXml;
            set
            {
                if (!value && value != isLoadingModXml)
                    CurrentLoadingMod = null;
                isLoadingModXml = value;
            }
        }

        private static bool isLoadingPatchXml;
        public static bool IsLoadingPatchXML
        {
            get => isLoadingPatchXml;
            set
            {
                if (!value && value != isLoadingPatchXml)
                    CurrentLoadingMod = null;
                isLoadingPatchXml = value;
            }
        }

        public static bool IsRecovering;
        public static bool LoadingFinished;
        public static ModContentPack Core;
        public static GagarinSettings Settings;
        public static Dictionary<XmlNode, LoadableXmlAsset> DefsXmlAssets = new Dictionary<XmlNode, LoadableXmlAsset>();
        public static Dictionary<string, LoadableXmlAsset> XmlAssets = new Dictionary<string, LoadableXmlAsset>();
        public static List<ModContentPack> RunningMods = new List<ModContentPack>();
        public static HashSet<string> Assets = new HashSet<string>();
        public static Dictionary<string, string> AssetsHashes = new Dictionary<string, string>();
        public static Dictionary<string, ulong> AssetsHashesInt = new Dictionary<string, ulong>();
        public static ModContentPack CurrentLoadingMod;
    }
}
