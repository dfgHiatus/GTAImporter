using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace AssetImportAPI
{
    public static class Utils
    {
        /// <summary>
        /// Utility method to identify unicode characters in a given string.
        /// </summary>
        /// <param name="input">The string to test against</param>
        /// <returns>A boolean indicating if the string contains a unicode character.</returns>
        public static bool ContainsUnicodeCharacter(string input)
        {
            const int MaxAnsiCode = 255;
            return input.Any(c => c > MaxAnsiCode);
        }

        /// <summary>
        /// Utility method to generate a MD5 hash for a given filepath.
        /// Credit to delta for this method https://github.com/XDelta/
        /// </summary>
        /// <param name="filepath">The filepath to generate an MD5 for.</param>
        /// <returns>A MD5 representation of the string.</returns>
        public static string GenerateMD5(string filepath)
        {
            using var hasher = MD5.Create();
            using var stream = File.OpenRead(filepath);
            var hash = hasher.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "");
        }
    }
}
