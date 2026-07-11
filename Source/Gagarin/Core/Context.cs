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
        private static bool _isUsingCache;

        public static bool IsUsingCache
        {
            get => _isUsingCache;
            set
            {
                if (!value && value != _isUsingCache)
                {
                    GagarinCacheManager.InvalidateXmlCache();
                    StackTrace trace = new StackTrace(1);
                    StringBuilder builder = new StringBuilder();
                    builder.Append("GAGARIN: <color=yellow>Cache disabled from</color>");
                    for (int i = 0; i < trace.FrameCount; i++)
                    {
                        StackFrame frame = trace.GetFrame(i);
                        builder.AppendInNewLine(
                            $"{frame.GetMethod().DeclaringType?.FullName}:{frame.GetMethod().Name}():{frame.GetFileLineNumber()}");
                    }
                    Log.Warning(builder.ToString());
                    MissileGirl.Logger.Debug(builder.ToString());
                }
                _isUsingCache = value;
            }
        }

        private static bool _isLoadingModXML;

        public static bool IsLoadingModXML
        {
            get => _isLoadingModXML;
            set
            {
                if (!value && value != _isLoadingModXML)
                    CurrentLoadingMod = null;
                _isLoadingModXML = value;
            }
        }

        private static bool _isLoadingPatchXML;

        public static bool IsLoadingPatchXML
        {
            get => _isLoadingPatchXML;
            set
            {
                if (!value && value != _isLoadingPatchXML)
                    CurrentLoadingMod = null;
                _isLoadingPatchXML = value;
            }
        }

        public static bool IsRecovering;
        public static bool LoadingFinished;
        public static ModContentPack Core;
        public static GagarinSettings Settings;
        public static Dictionary<XmlNode, LoadableXmlAsset> DefsXmlAssets = new();
        public static Dictionary<string, LoadableXmlAsset> XmlAssets = new();
        public static List<ModContentPack> RunningMods = new();
        public static HashSet<string> Assets = new();
        public static Dictionary<string, string> AssetsHashes = new();
        public static Dictionary<string, ulong> AssetsHashesInt = new();
        public static ModContentPack CurrentLoadingMod;
    }
}
