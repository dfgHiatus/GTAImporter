using BaseX;
using CodeX;
using FrooxEngine;
using HarmonyLib;
using NeosModLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AssetImportAPI
{
    public class MyAssetImporter : NeosMod
    {
        public override string Name => "AssetImporterTemplate";
        public override string Author => "dfgHiatus";
        public override string Version => "1.0.0";
        public override string Link => "https://github.com/dfgHiatus/AssetImportAPI";

        public static string cachePath = Path.Combine(Engine.Current.CachePath, "Cache", "DecompressedCustomPackages");
        public static string customFileExtension = null; // <= !!! CHANGE ME!!!

        private static ModConfiguration config;
        public override void DefineConfiguration(ModConfigurationDefinitionBuilder builder)
        {
            builder
                .Version(new Version(1, 0, 0))
                .AutoSave(true);
        }

        [AutoRegisterConfigKey]
        private static ModConfigurationKey<bool> importAsRawFiles =
             new("importAsRawFiles",
             "Import files directly into Neos. Archives can be very large, keep this true unless you know what you're doing!",
             () => true);
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<bool> importText =
            new("importText", "Import Text", () => true);
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<bool> importTexture =
            new("importTexture", "Import Textures", () => true);
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<bool> importDocument =
            new("importDocument", "Import Documents", () => true);
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<bool> importMesh =
            new("importMesh", "Import Mesh", () => true);
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<bool> importPointCloud =
            new("importPointCloud", "Import Point Clouds", () => true);
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<bool> importAudio =
            new("importAudio", "Import Audio", () => true);
        [AutoRegisterConfigKey]
        public static ModConfigurationKey<bool> importFont =
            new("importFont", "Import Fonts", () => true);
        [AutoRegisterConfigKey]
        public static ModConfigurationKey<bool> importVideo =
            new("importVideo", "Import Videos", () => true);

        /// <summary>
        /// <para>
        /// If wanting to import a certain FILE type, pass the lowercase file extension WITHOUT the period into customFileExtension.
        /// </para>
        /// <para>
        /// If wanting to unzip ARCHIVES, such as VPKs or UnityPackages you'll want to create a directory to store them.
        /// </para>
        /// <example> For instance, if you wanted to import .midi files you would pass "midi".</example>
        /// </summary>
        public override void OnEngineInit()
        {
            new Harmony("net.dfgHiatus.AssetImporterTemplate").PatchAll();
            config = GetConfiguration();

            Engine.Current.RunPostInit(() => AssetPatch(customFileExtension));
            Directory.CreateDirectory(cachePath);
        }

        /// <summary>
        /// (Optionally) make Neos respect custom file extensions.
        /// </summary>
        /// <param name="fileExtension"> The name of the file extension to patch in.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when the supplied file extension is:
        /// 1) Null
        /// 2) Empty
        /// 3) Contains a Unicode character
        /// </exception>
        private static void AssetPatch(string fileExtension)
        {
            if (string.IsNullOrEmpty(fileExtension) && !Utils.ContainsUnicodeCharacter(fileExtension))
            {
                var aExt = Traverse.Create(typeof(AssetHelper)).Field<Dictionary<AssetClass, List<string>>>("associatedExtensions");
                aExt.Value[AssetClass.Special].Add(fileExtension);
            }
            else
            {
                throw new ArgumentException($"Supplied file extension {fileExtension} was invalid.");
            }
        }

        /// <summary>
        /// For single file imports, use the supplied template. Likewise for archives.
        /// </summary>
        [HarmonyPatch(typeof(UniversalImporter), "Import", typeof(AssetClass), typeof(IEnumerable<string>),
           typeof(World), typeof(float3), typeof(floatQ), typeof(bool))]
        public class UniversalImporterPatch
        {
            static bool Prefix(ref IEnumerable<string> files, ref Task __result)
            {
                // For single-file imports
                var query = files.Where(x => x.ToLower().EndsWith(customFileExtension));
                if (query.Count() > 0)
                {
                    __result = ProcessCustomImport(query);
                }

                // For archive imports
                List<string> isPackage = new();
                List<string> notPackage = new();
                foreach (var file in files)
                {
                    if (Path.GetExtension(file).ToLower().Equals(customFileExtension))
                        isPackage.Add(file);
                    else
                        notPackage.Add(file);
                }

                List<string> allDirectoriesToBatchImport = new();
                foreach (var dir in ArchiveExtractor.DecomposeArchives(isPackage.ToArray()))
                    allDirectoriesToBatchImport.AddRange(Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                        .Where(ShouldImportFile).ToArray());

                var slot = Engine.Current.WorldManager.FocusedWorld.AddSlot(customFileExtension + " package import");
                slot.PositionInFrontOfUser();
                BatchFolderImporter.BatchImport(slot, allDirectoriesToBatchImport, config.GetValue(importAsRawFiles));

                if (notPackage.Count <= 0) return false;
                files = notPackage.ToArray();

                return true;
            }
        }

        /// <summary>
        /// Asynchronously import single files. 
        /// To handle expensive tasks, use <example>await default(ToBackground)</example>.
        /// To perform operations in current world, and to import the actual file use <example>await default(ToWorld)</example>.
        /// Method subcalls from this method need to be asynchronous.
        /// </summary>
        /// <param name="files">A copy of the files a user has selected for import.</param>
        private static async Task ProcessCustomImport(IEnumerable<string> files)
        {
            throw new NotImplementedException("File import method needs to be defined!");
            await default(ToBackground);
            await default(ToWorld);
        }

        /// <summary>
        /// Utility method to discriminate file types on import. Useful when dealing with arbitrarily large archives.
        /// Will handle recursive cases.
        /// </summary>
        /// <para>
        /// This uses the user's active mod config to change import settings.
        /// </para>
        /// <param name="file">The canidate file to test against</param>
        /// <returns>A boolean indicating if the file should be imported.</returns>
        private static bool ShouldImportFile(string file)
        {
            var assetClass = AssetHelper.ClassifyExtension(Path.GetExtension(file));
            return (config.GetValue(importText) && assetClass == AssetClass.Text) ||
            (config.GetValue(importTexture) && assetClass == AssetClass.Texture) ||
            (config.GetValue(importDocument) && assetClass == AssetClass.Document) ||
            (config.GetValue(importMesh) && assetClass == AssetClass.Model
                && Path.GetExtension(file).ToLower() != ".xml") ||
            (config.GetValue(importPointCloud) && assetClass == AssetClass.PointCloud) ||
            (config.GetValue(importAudio) && assetClass == AssetClass.Audio) ||
            (config.GetValue(importFont) && assetClass == AssetClass.Font) ||
            (config.GetValue(importVideo) && assetClass == AssetClass.Video) ||
                Path.GetExtension(file).ToLower().EndsWith(customFileExtension);
        }
    }
}
