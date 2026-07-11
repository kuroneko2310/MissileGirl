using System;
using System.IO;
using HarmonyLib;
using MissileGirl;
using Verse;

namespace Gagarin
{
    public static class LoadableXmlAsset_Constructor_Patch
    {
        private static readonly object HashSync = new object();

        [Main.OnInitialization]
        public static void Start()
        {
            Finder.Harmony.Patch(
                AccessTools.Constructor(typeof(LoadableXmlAsset), new[] { typeof(string), typeof(string) }),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(LoadableXmlAsset_Constructor_Patch),
                    nameof(Postfix_String))));

            Finder.Harmony.Patch(
                AccessTools.Constructor(typeof(LoadableXmlAsset), new[] { typeof(FileInfo), typeof(ModContentPack) }),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(LoadableXmlAsset_Constructor_Patch),
                    nameof(Postfix_FileInfo))));
        }

        public static void Postfix_String(LoadableXmlAsset __instance, string name, string text)
        {
            if (!ShouldProcess())
                return;

            try
            {
                ProcessHashes(__instance,
                    AssetHashingUtility.CalculateHashMd5(text),
                    AssetHashingUtility.CalculateHash(text));
            }
            catch (Exception exception)
            {
                DisableCacheAfterHashFailure(exception);
            }
        }

        public static void Postfix_FileInfo(LoadableXmlAsset __instance, FileInfo file, ModContentPack mod)
        {
            if (!ShouldProcess())
                return;

            try
            {
                AssetHashingUtility.CalculateFileHashes(file.FullName, out string current, out ulong currentInt);
                ProcessHashes(__instance, current, currentInt);
            }
            catch (Exception exception)
            {
                DisableCacheAfterHashFailure(exception);
            }
        }

        private static bool ShouldProcess() => Context.IsLoadingModXML || Context.IsLoadingPatchXML;

        private static void ProcessHashes(LoadableXmlAsset asset, string current, ulong currentInt)
        {
            string id = asset.GetLoadableId();
            lock (HashSync)
            {
                bool changed = !Context.AssetsHashes.TryGetValue(id, out string old)
                               || !Context.AssetsHashesInt.TryGetValue(id, out ulong oldInt)
                               || !string.Equals(current, old, StringComparison.Ordinal)
                               || oldInt != currentInt;

                if (changed)
                {
                    try
                    {
                        if (GagarinEnvironmentInfo.CacheExists && Context.IsUsingCache)
                        {
                            string message = Context.IsLoadingPatchXML ? "Patch changed" : "XML asset changed";
                            Log.Warning($"GAGARIN: {message}: <color=red>{asset.name}</color> "
                                        + $"from <color=red>{Context.CurrentLoadingMod?.PackageId ?? "Unknown"}</color> "
                                        + $"in {asset.fullFolderPath}");
                        }
                    }
                    finally
                    {
                        Context.IsUsingCache = false;
                    }
                }

                Context.Assets.Add(id);
                Context.AssetsHashes[id] = current;
                Context.AssetsHashesInt[id] = currentInt;
            }
        }

        private static void DisableCacheAfterHashFailure(Exception exception)
        {
            Context.IsUsingCache = false;
            Logger.Debug("GAGARIN: Failed while hashing LoadableXmlAsset", exception: exception);
        }
    }
}
