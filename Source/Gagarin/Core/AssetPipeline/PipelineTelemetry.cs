using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using Verse;

namespace Gagarin
{
    internal static class PipelineTelemetry
    {
        private static readonly Stopwatch StartupStopwatch = new Stopwatch();
        private static long initializationMilliseconds;

        public static void BeginStartup()
        {
            initializationMilliseconds = 0;
            StartupStopwatch.Restart();
        }

        public static void RecordInitializationComplete()
        {
            initializationMilliseconds = StartupStopwatch.ElapsedMilliseconds;
        }

        public static void RecordStartupComplete(bool usedXmlCache, string generation, string plan, string textureStats)
        {
            if (!GagarinPrefs.RecordAssetPipelineTelemetry)
                return;

            try
            {
                StartupStopwatch.Stop();
                var document = new XmlDocument();
                XmlElement root;
                if (File.Exists(GagarinEnvironmentInfo.TelemetryFilePath))
                {
                    var settings = new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Prohibit,
                        IgnoreComments = true,
                        IgnoreWhitespace = true,
                        XmlResolver = null
                    };
                    using (var reader = XmlReader.Create(GagarinEnvironmentInfo.TelemetryFilePath, settings))
                        document.Load(reader);
                    root = document.DocumentElement;
                    if (root == null || root.Name != "AssetPipelineTelemetry")
                        throw new InvalidDataException("Invalid telemetry root");
                }
                else
                {
                    root = document.CreateElement("AssetPipelineTelemetry");
                    document.AppendChild(root);
                }

                var session = document.CreateElement("Session");
                session.SetAttribute("utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                session.SetAttribute("startupMs", StartupStopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
                session.SetAttribute("pipelineInitMs", initializationMilliseconds.ToString(CultureInfo.InvariantCulture));
                session.SetAttribute("usedXmlCache", usedXmlCache.ToString());
                session.SetAttribute("generation", generation ?? string.Empty);
                session.SetAttribute("plan", plan ?? string.Empty);
                session.SetAttribute("textureStats", textureStats ?? string.Empty);
                root.AppendChild(session);

                while (root.ChildNodes.OfType<XmlElement>().Count() > 50)
                    root.RemoveChild(root.FirstChild);

                AtomicFile.SaveXml(GagarinEnvironmentInfo.TelemetryFilePath, document);
            }
            catch (Exception exception)
            {
                Log.Warning($"GAGARIN: Could not record asset-pipeline telemetry.\n{exception}");
            }
        }
    }
}
