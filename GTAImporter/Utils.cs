using System;
using System.IO;
using System.Security.Cryptography;

namespace GTAVImporter
{
    public static class Utils
    { 
        public static string GenerateMD5(string filepath)
        {
            using var hasher = MD5.Create();
            using var stream = File.OpenRead(filepath);
            var hash = hasher.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "");
        }
    }
}
