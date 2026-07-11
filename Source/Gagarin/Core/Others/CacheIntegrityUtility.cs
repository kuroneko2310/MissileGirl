using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Gagarin
{
    internal static class CacheIntegrityUtility
    {
        public static void WriteSha256(string sourcePath, string hashPath)
        {
            string hash = CalculateSha256(sourcePath);
            AtomicFile.Write(hashPath, temporaryPath =>
                File.WriteAllText(temporaryPath, hash, new UTF8Encoding(false)));
        }

        public static bool ValidateSha256(string sourcePath, string hashPath)
        {
            if (!File.Exists(sourcePath) || !File.Exists(hashPath))
                return false;

            string expected;
            try
            {
                expected = File.ReadAllText(hashPath, Encoding.UTF8).Trim();
            }
            catch
            {
                return false;
            }

            if (expected.Length != 64)
                return false;

            string actual = CalculateSha256(sourcePath);
            return FixedTimeEquals(expected, actual);
        }

        private static string CalculateSha256(string path)
        {
            using SHA256 sha = SHA256.Create();
            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;

            int difference = 0;
            for (int i = 0; i < left.Length; i++)
                difference |= left[i] ^ right[i];
            return difference == 0;
        }
    }
}
