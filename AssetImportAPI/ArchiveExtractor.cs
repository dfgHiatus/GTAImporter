using BaseX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AssetImportAPI
{
    public class ArchiveExtractor
    {
        /// <summary>
        /// <para>
        /// Accepts an array of archives as input and for each:
        /// </para>
        /// <para>
        /// 1) Generates a unique MD5 hash, using the archive itself as a seed.
        /// </para>
        /// <para>
        /// 2) Creates and names a directory inside MyAssetImporter.cachePath using a string representation of hash
        /// </para>
        /// <para>
        /// 3) Directs the "Unpack" method in this file to extract the contents of the particular archive into this folder
        /// </para>
        /// </summary>
        /// <param name="files">The input archives to process.</param>
        public static string[] DecomposeArchives(string[] files)
        {
            var fileToHash = files.ToDictionary(file => file, Utils.GenerateMD5);
            HashSet<string> dirsToImport = new();
            HashSet<string> archivesToDecompress = new();
            foreach (var element in fileToHash)
            {
                var dir = Path.Combine(MyAssetImporter.cachePath, element.Value);
                if (!Directory.Exists(dir))
                    archivesToDecompress.Add(element.Key);
                else
                    dirsToImport.Add(dir);
            }
            foreach (var package in archivesToDecompress)
            {
                var packageName = Path.GetFileNameWithoutExtension(package);
                if (Utils.ContainsUnicodeCharacter(packageName))
                {
                    UniLog.Error($"Imported archive {packageName} cannot have unicode characters in its file name.");
                    continue;
                }
                var extractedPath = Path.Combine(MyAssetImporter.cachePath, fileToHash[package]);
                Directory.CreateDirectory(extractedPath);
                Unpack(package, extractedPath);
                dirsToImport.Add(extractedPath);
            }
            return dirsToImport.ToArray();
        }

        /// <summary>
        /// Extracts the archive into a unique folder. This method must be implemented!
        /// </summary>
        /// <param name="package">The absolute path of the archive to extract.</param>
        /// <param name="outputPath">The absolute path of the unique MD5-hashed directory to extract the archive to.</param>
        private static void Unpack(string package, string outputPath)
        {
            throw new NotImplementedException("Package decompression method needs to be defined!");
        }
    }
}
