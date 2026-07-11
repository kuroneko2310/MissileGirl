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

            var verification = new XmlDocument();
            verification.Load(destinationPath);
            if (verification.DocumentElement == null)
                throw new InvalidDataException($"Atomic XML write produced no root element: {destinationPath}");
        }

        public static void Copy(string sourcePath, string destinationPath)
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("Source file does not exist", sourcePath);

            Write(destinationPath, target =>
            {
                using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
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

            var temporaryPath = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N");
            var backupPath = destinationPath + ".bak";

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
                        if (File.Exists(backupPath))
                            File.Delete(backupPath);
                        File.Replace(temporaryPath, destinationPath, backupPath, true);
                        if (File.Exists(backupPath))
                            File.Delete(backupPath);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        ReplaceByMove(temporaryPath, destinationPath);
                    }
                    catch (IOException)
                    {
                        ReplaceByMove(temporaryPath, destinationPath);
                    }
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

        private static void ReplaceByMove(string temporaryPath, string destinationPath)
        {
            var oldPath = destinationPath + ".old-" + Guid.NewGuid().ToString("N");
            if (File.Exists(destinationPath))
                File.Move(destinationPath, oldPath);

            try
            {
                File.Move(temporaryPath, destinationPath);
                if (File.Exists(oldPath))
                    File.Delete(oldPath);
            }
            catch
            {
                if (!File.Exists(destinationPath) && File.Exists(oldPath))
                    File.Move(oldPath, destinationPath);
                throw;
            }
        }
    }
}
