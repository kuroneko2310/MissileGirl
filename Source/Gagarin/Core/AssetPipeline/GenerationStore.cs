using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;

namespace Gagarin
{
    internal sealed class GenerationStore
    {
        private const string ReadyFileName = "READY";
        private const string BrokenFileName = "BROKEN";
        public string ActiveGenerationName { get; private set; }
        public string ActiveGenerationPath => string.IsNullOrEmpty(ActiveGenerationName) ? null : Path.Combine(GagarinEnvironmentInfo.GenerationsFolderPath, ActiveGenerationName);

        public void Initialize()
        {
            Directory.CreateDirectory(GagarinEnvironmentInfo.GenerationsFolderPath);
            ActiveGenerationName = ReadActiveName();
            if (!IsReady(ActiveGenerationPath))
                ActiveGenerationName = FindNewestReadyGeneration();

            if (!string.IsNullOrEmpty(ActiveGenerationName))
                AtomicFile.WriteAllText(GagarinEnvironmentInfo.ActiveGenerationFilePath, ActiveGenerationName);
            else
                DeleteIfExists(GagarinEnvironmentInfo.ActiveGenerationFilePath);
        }

        public bool RestoreActiveLegacyView()
        {
            Initialize();
            if (!IsReady(ActiveGenerationPath))
            {
                InvalidateXmlView();
                return false;
            }

            try
            {
                RestoreFile("Unified.xml", GagarinEnvironmentInfo.UnifiedXmlFilePath, true);
                RestoreFile("Unified_Original.xml", GagarinEnvironmentInfo.UnifiedPatchedOriginalXmlPath);
                RestoreFile("AssetsHash.xml", GagarinEnvironmentInfo.HashFilePath);
                RestoreFile("AssetsHashInt.xml", GagarinEnvironmentInfo.HashFilePathInt);
                RestoreFile("ModList.xml", GagarinEnvironmentInfo.ModListFilePath);
                return true;
            }
            catch (Exception exception)
            {
                Log.Warning($"GAGARIN: Active cache generation '{ActiveGenerationName}' could not be restored.\n{exception}");
                MarkActiveBroken(exception.Message);
                return TryRollback();
            }
        }

        public string GetActiveManifestPath()
        {
            if (!IsReady(ActiveGenerationPath)) return null;
            var manifest = Path.Combine(ActiveGenerationPath, "manifest.xml");
            return File.Exists(manifest) ? manifest : null;
        }

        public void Commit(AssetPipelineManifest manifest, int generationsToKeep)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (!File.Exists(GagarinEnvironmentInfo.UnifiedXmlFilePath)) throw new FileNotFoundException("Unified XML cache is not available for generation commit.", GagarinEnvironmentInfo.UnifiedXmlFilePath);
            Directory.CreateDirectory(GagarinEnvironmentInfo.GenerationsFolderPath);
            var generationName = "gen-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var temporaryPath = Path.Combine(GagarinEnvironmentInfo.GenerationsFolderPath, generationName + ".tmp");
            var generationPath = Path.Combine(GagarinEnvironmentInfo.GenerationsFolderPath, generationName);
            Directory.CreateDirectory(temporaryPath);
            try
            {
                CopyIfExists(GagarinEnvironmentInfo.UnifiedXmlFilePath, Path.Combine(temporaryPath, "Unified.xml"), true);
                CopyIfExists(GagarinEnvironmentInfo.UnifiedPatchedOriginalXmlPath, Path.Combine(temporaryPath, "Unified_Original.xml"));
                CopyIfExists(GagarinEnvironmentInfo.HashFilePath, Path.Combine(temporaryPath, "AssetsHash.xml"));
                CopyIfExists(GagarinEnvironmentInfo.HashFilePathInt, Path.Combine(temporaryPath, "AssetsHashInt.xml"));
                CopyIfExists(GagarinEnvironmentInfo.ModListFilePath, Path.Combine(temporaryPath, "ModList.xml"));
                manifest.Save(Path.Combine(temporaryPath, "manifest.xml"));
                ValidateGeneration(temporaryPath);
                AtomicFile.WriteAllText(Path.Combine(temporaryPath, ReadyFileName), $"schema={AssetPipelineManifest.CurrentSchemaVersion}\ncreated={DateTime.UtcNow:o}\n");
                Directory.Move(temporaryPath, generationPath);
                AtomicFile.WriteAllText(GagarinEnvironmentInfo.ActiveGenerationFilePath, generationName);
                ActiveGenerationName = generationName;
                Prune(Math.Max(2, generationsToKeep));
            }
            catch
            {
                if (Directory.Exists(temporaryPath)) Directory.Delete(temporaryPath, true);
                throw;
            }
        }

        public void MarkActiveBroken(string reason)
        {
            var path = ActiveGenerationPath;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            try
            {
                var ready = Path.Combine(path, ReadyFileName);
                if (File.Exists(ready)) File.Delete(ready);
                AtomicFile.WriteAllText(Path.Combine(path, BrokenFileName), DateTime.UtcNow.ToString("o") + "\n" + (reason ?? "Unknown failure"));
            }
            catch (Exception exception) { Log.Warning($"GAGARIN: Could not mark generation '{ActiveGenerationName}' broken.\n{exception}"); }
        }

        public bool TryRollback()
        {
            var current = ActiveGenerationName;
            foreach (var candidate in EnumerateReadyGenerations()
                         .Where(name => !string.Equals(name, current, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(name => name, StringComparer.Ordinal))
            {
                ActiveGenerationName = candidate;
                try
                {
                    AtomicFile.WriteAllText(GagarinEnvironmentInfo.ActiveGenerationFilePath, candidate);
                    if (RestoreActiveLegacyViewWithoutInitialize())
                        return true;

                    MarkActiveBroken("Generation stopped being READY during rollback.");
                }
                catch (Exception exception)
                {
                    Log.Warning($"GAGARIN: Previous generation '{candidate}' is also unusable.\n{exception}");
                    MarkActiveBroken(exception.Message);
                }
            }

            ActiveGenerationName = null;
            DeleteIfExists(GagarinEnvironmentInfo.ActiveGenerationFilePath);
            InvalidateXmlView();
            return false;
        }

        public void InvalidateXmlView()
        {
            DeleteIfExists(GagarinEnvironmentInfo.UnifiedXmlFilePath);
            DeleteIfExists(GagarinEnvironmentInfo.UnifiedPatchedOriginalXmlPath);
            DeleteIfExists(GagarinEnvironmentInfo.HashFilePath);
            DeleteIfExists(GagarinEnvironmentInfo.HashFilePathInt);
            DeleteIfExists(GagarinEnvironmentInfo.ModListFilePath);
        }

        public void ClearAllGenerations()
        {
            InvalidateXmlView();
            if (Directory.Exists(GagarinEnvironmentInfo.GenerationsFolderPath)) Directory.Delete(GagarinEnvironmentInfo.GenerationsFolderPath, true);
            Directory.CreateDirectory(GagarinEnvironmentInfo.GenerationsFolderPath);
            DeleteIfExists(GagarinEnvironmentInfo.ActiveGenerationFilePath);
            ActiveGenerationName = null;
        }

        public void Prune(int generationsToKeep)
        {
            var keep = Math.Max(2, generationsToKeep);
            if (!Directory.Exists(GagarinEnvironmentInfo.GenerationsFolderPath))
                return;

            var generations = Directory.GetDirectories(GagarinEnvironmentInfo.GenerationsFolderPath, "gen-*")
                .Where(path => !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToList();

            var ready = generations.Where(IsReady).ToList();
            var retainedReady = new HashSet<string>(
                ready.Take(keep).Select(Path.GetFileName),
                StringComparer.OrdinalIgnoreCase);

            if (IsReady(ActiveGenerationPath))
                retainedReady.Add(ActiveGenerationName);

            foreach (var path in ready)
            {
                if (retainedReady.Contains(Path.GetFileName(path)))
                    continue;
                DeleteGeneration(path, "old READY");
            }

            foreach (var path in generations.Where(path => Directory.Exists(path) && !IsReady(path)).Skip(2))
                DeleteGeneration(path, "old BROKEN/incomplete");

            foreach (var temporary in Directory.GetDirectories(GagarinEnvironmentInfo.GenerationsFolderPath, "*.tmp"))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(temporary) < DateTime.UtcNow.AddDays(-1))
                        Directory.Delete(temporary, true);
                }
                catch
                {
                    // A concurrently written temporary generation is allowed to remain.
                }
            }
        }

        private bool RestoreActiveLegacyViewWithoutInitialize()
        {
            if (!IsReady(ActiveGenerationPath)) return false;
            RestoreFile("Unified.xml", GagarinEnvironmentInfo.UnifiedXmlFilePath, true);
            RestoreFile("Unified_Original.xml", GagarinEnvironmentInfo.UnifiedPatchedOriginalXmlPath);
            RestoreFile("AssetsHash.xml", GagarinEnvironmentInfo.HashFilePath);
            RestoreFile("AssetsHashInt.xml", GagarinEnvironmentInfo.HashFilePathInt);
            RestoreFile("ModList.xml", GagarinEnvironmentInfo.ModListFilePath);
            return true;
        }

        private void RestoreFile(string generationFileName, string destinationPath, bool required = false)
        {
            var source = Path.Combine(ActiveGenerationPath, generationFileName);
            if (File.Exists(source))
            {
                AtomicFile.Copy(source, destinationPath);
                return;
            }

            DeleteIfExists(destinationPath);
            if (required)
                throw new InvalidDataException("Generation is missing " + generationFileName);
        }

        private static void CopyIfExists(string sourcePath, string destinationPath, bool required = false)
        {
            if (File.Exists(sourcePath)) { File.Copy(sourcePath, destinationPath, true); return; }
            if (required) throw new FileNotFoundException("Required generation file is missing", sourcePath);
        }

        private static void ValidateGeneration(string path)
        {
            var unified = Path.Combine(path, "Unified.xml");
            var manifest = Path.Combine(path, "manifest.xml");
            if (!File.Exists(unified) || new FileInfo(unified).Length == 0) throw new InvalidDataException("Generation Unified.xml is missing or empty.");
            if (!File.Exists(manifest) || AssetPipelineManifest.Load(manifest) == null) throw new InvalidDataException("Generation manifest is missing or invalid.");
        }

        private string ReadActiveName()
        {
            try
            {
                if (!File.Exists(GagarinEnvironmentInfo.ActiveGenerationFilePath)) return null;
                var name = File.ReadAllText(GagarinEnvironmentInfo.ActiveGenerationFilePath).Trim();
                if (string.IsNullOrEmpty(name) || name == "." || name == ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    return null;
                return name;
            }
            catch { return null; }
        }

        private string FindNewestReadyGeneration() => EnumerateReadyGenerations().OrderByDescending(name => name, StringComparer.Ordinal).FirstOrDefault();

        private IEnumerable<string> EnumerateReadyGenerations()
        {
            if (!Directory.Exists(GagarinEnvironmentInfo.GenerationsFolderPath)) yield break;
            foreach (var directory in Directory.GetDirectories(GagarinEnvironmentInfo.GenerationsFolderPath, "gen-*"))
                if (IsReady(directory))
                    yield return Path.GetFileName(directory);
        }

        private static void DeleteGeneration(string path, string kind)
        {
            try
            {
                Directory.Delete(path, true);
            }
            catch (Exception exception)
            {
                Log.Warning($"GAGARIN: Could not prune {kind} generation '{path}'.\n{exception}");
            }
        }

        private static bool IsReady(string path) => !string.IsNullOrEmpty(path) && Directory.Exists(path) && File.Exists(Path.Combine(path, ReadyFileName)) && File.Exists(Path.Combine(path, "Unified.xml")) && File.Exists(Path.Combine(path, "manifest.xml"));
        private static void DeleteIfExists(string path) { if (File.Exists(path)) File.Delete(path); }
    }
}
