using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Gagarin
{
    internal static class PipelineHash
    {
        public static string FileSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                return ToHex(sha.ComputeHash(stream));
        }

        public static string BytesSha256(byte[] data)
        {
            using (var sha = SHA256.Create())
                return ToHex(sha.ComputeHash(data ?? Array.Empty<byte>()));
        }

        public static string TextSha256(string text)
        {
            return BytesSha256(Encoding.UTF8.GetBytes(text ?? string.Empty));
        }

        public static string Aggregate(IEnumerable<string> values)
        {
            var ordered = values == null
                ? Array.Empty<string>()
                : values.Where(value => value != null).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            return TextSha256(string.Join("\n", ordered));
        }

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch
            {
                full = path;
            }

            return full.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes)
                builder.Append(value.ToString("x2"));
            return builder.ToString();
        }
    }
}
