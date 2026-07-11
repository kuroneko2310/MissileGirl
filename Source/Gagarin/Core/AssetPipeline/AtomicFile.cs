using System;
using System.IO;
using System.Text;
using System.Xml;

namespace Gagarin
{
    internal static class AtomicFile
    {
        public static void WriteAllText(string destinationPath, string content, Encoding encoding = null)
        {
            encoding ??= new UTF8Encoding(false);
            Write(destinationPath, stream =>
            {
                using (var writer = new StreamWriter(stream, encoding, 4096, true))
                {
                    writer.Write(content ?? string.Empty);
                    writer.Flush();
                }
            });
        }

        public static void WriteAllBytes(string destinationPath, byte[] content)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            Write(destinationPath, stream => stream.Write(content, 0, content.Length));
        }

        public static void SaveXml(string destinationPath, XmlDocument document, XmlWriterSettings settings = null)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            settings ??= new XmlWriterSettings
            {
                CheckCharacters = false,
                Indent = true,
                NewLineChars = "\n",
                Encoding = new UTF8Encoding(false)
            };

            Write(destinationPath, stream =>
            {
                using (var writer = XmlWriter.Create(stream, settings))
                    document.Save(writer);
            });

            var verification = new XmlDocument { XmlResolver = null };
            var readerSettings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            using (var reader = XmlReader.Create(destinationPath, readerSettings))
                verification.Load(reader);
            if (verification.DocumentElement == null)
                throw new InvalidDataException($"Atomic XML write produced no root element: {destinationPath}");
        }

        public static void Copy(string sourcePath, string destinationPath)
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("Source file does not exist", sourcePath);

            Write(destinationPath, target =>
            {
                using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    source.CopyTo(target);
            });
        }

        public static void Write(string destinationPath, Action<FileStream> writer)
        {
            if (string.IsNullOrEmpty(destinationPath))
                throw new ArgumentException("Destination path is required", nameof(destinationPath));
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var operationId = Guid.NewGuid().ToString("N");
            var temporaryPath = destinationPath + ".tmp-" + operationId;
            var backupPath = destinationPath + ".bak-" + operationId;

            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           64 * 1024,
                           FileOptions.WriteThrough))
                {
                    writer(stream);
                    stream.Flush(true);
                }

                if (File.Exists(destinationPath))
                {
                    try
                    {
                        File.Replace(temporaryPath, destinationPath, backupPath, true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        ReplaceByMoveIfTemporaryExists(temporaryPath, destinationPath);
                    }
                    catch (NotSupportedException)
                    {
                        ReplaceByMoveIfTemporaryExists(temporaryPath, destinationPath);
                    }
                    catch (IOException)
                    {
                        ReplaceByMoveIfTemporaryExists(temporaryPath, destinationPath);
                    }
                    finally
                    {
                        DeleteBestEffort(backupPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, destinationPath);
                }
            }
            finally
            {
                DeleteBestEffort(temporaryPath);
                DeleteBestEffort(backupPath);
            }
        }

        private static void ReplaceByMoveIfTemporaryExists(string temporaryPath, string destinationPath)
        {
            if (!File.Exists(temporaryPath))
                throw new IOException("Atomic replacement consumed the temporary file before reporting failure.");
            ReplaceByMove(temporaryPath, destinationPath);
        }

        private static void ReplaceByMove(string temporaryPath, string destinationPath)
        {
            var oldPath = destinationPath + ".old-" + Guid.NewGuid().ToString("N");
            if (File.Exists(destinationPath))
                File.Move(destinationPath, oldPath);

            try
            {
                File.Move(temporaryPath, destinationPath);
                DeleteBestEffort(oldPath);
            }
            catch
            {
                if (!File.Exists(destinationPath) && File.Exists(oldPath))
                    File.Move(oldPath, destinationPath);
                throw;
            }
        }

        private static void DeleteBestEffort(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // A stale temporary/backup is safer than reporting a completed replacement as failed.
            }
        }
    }
}
