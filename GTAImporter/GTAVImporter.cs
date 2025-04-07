using Elements.Core;
using Elements.Assets;
using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GTAVImporter;

public class GTAVImporter : ResoniteMod
{
    public override string Name => "GTAV-Importer";
    public override string Author => "dfgHiatus";
    public override string Version => "2.0.0";
    public override string Link => "https://github.com/dfgHiatus/GTAImporter";

    public static string cachePath = Path.Combine(Engine.Current.CachePath, "Cache", "DecompressedGTAArchives");
    public static string[] customFileExtensions = new string[] { "odr", "odd" };

    public static ModConfiguration config;

    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<bool> importText =
        new("importText", "Import Text", () => true);
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<bool> importTexture =
        new("importTexture", "Import Textures", () => true);
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<bool> importDocument =
        new("importDocument", "Import Documents", () => true);
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<bool> importMesh =
        new("importMesh", "Import Mesh", () => true);
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<bool> importPointCloud =
        new("importPointCloud", "Import Point Clouds", () => true);
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<bool> importAudio =
        new("importAudio", "Import Audio", () => true);
    [AutoRegisterConfigKey]
    public static ModConfigurationKey<bool> importFont =
        new("importFont", "Import Fonts", () => true);
    [AutoRegisterConfigKey]
    public static ModConfigurationKey<bool> importVideo =
        new("importVideo", "Import Videos", () => true);

    public override void OnEngineInit()
    {
        new Harmony("net.dfgHiatus.GTAImporter").PatchAll();
        Directory.CreateDirectory(cachePath);
        config = GetConfiguration();
        Engine.Current.RunPostInit(() => AssetPatch(new string[] { "odr", "odd" }));
    }

    public static void AssetPatch(string[] extension)
    {
        Debug("Attempting to add MTL support to import system");

        // Get ImportExtension type via reflection since it's now a struct inside AssetHelper
        var assHelperType = typeof(AssetHelper);
        var importExtType = assHelperType.GetNestedType("ImportExtension",
            System.Reflection.BindingFlags.NonPublic);

        if (importExtType == null)
        {
            Error("ImportExtension type not found. This mod is toast.");
            return;
        }

        foreach (string extensionName in extension)
        {
            // Revised implementation using reflection to handle API changes
            try
            {
                // Create an ImportExtension instance with reflection
                // Constructor args: (string ext, bool autoImport)
                var importExt = System.Activator.CreateInstance(importExtType,
                    new object[] { extensionName, true });

                // Get the associatedExtensions field via reflection
                var extensionsField = assHelperType.GetField("associatedExtensions",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                if (extensionsField == null)
                {
                    Error("Could not find associatedExtensions field");
                    return;
                }

                // Get the dictionary and add our extension to the Special asset class
                var extensions = extensionsField.GetValue(null);
                var dictType = extensions.GetType();
                var specialValue = dictType.GetMethod("get_Item").Invoke(extensions, new object[] { AssetClass.Special });

                if (specialValue == null)
                {
                    Error("Couldn't get Special asset class list");
                    return;
                }

                // Add our ImportExtension to the list
                specialValue.GetType().GetMethod("Add").Invoke(specialValue, new[] { importExt });

                Debug("MTL import extension added successfully");
            }
            catch (System.Exception ex)
            {
                Error($"Failed to add MTL to special import formats: {ex}");
            }
        }
    }


    [HarmonyPatch(typeof(UniversalImporter), "ImportTask", typeof(AssetClass), typeof(IEnumerable<string>),
        typeof(World), typeof(float3), typeof(floatQ), typeof(float3), typeof(bool))]
    public class UniversalImporterPatch
    {
        public static bool Prefix(ref IEnumerable<string> files, ref Task __result, World world)
        {
            var query = files.Where(x =>
                x.EndsWith("odr", StringComparison.InvariantCultureIgnoreCase) ||
                x.EndsWith("odd", StringComparison.InvariantCultureIgnoreCase));

            if (query.Count() > 0)
            {
                Msg("Importing GTA.");
                __result = ProcessGTAImport(query, world);
            }
            
            return true;
        }
    }

    private static async Task ProcessGTAImport(IEnumerable<string> gtaFiles, World world)
    {
        await default(ToBackground);
        
        List<string> canidatesToBatchImport = new();
        foreach (var file in gtaFiles)
        {
            var canidateDir = Path.Combine(cachePath, Utils.GenerateMD5(file));
            
            if (Directory.Exists(canidateDir) && Directory.GetFiles(canidateDir).Length > 0)
            {
                canidatesToBatchImport.Add(canidateDir);
            }
            else
            {
                Directory.CreateDirectory(canidateDir);
                GTAToGLB(file.Replace(@"\", @"\\"), canidateDir.Replace(@"\", @"\\"), Path.GetFileNameWithoutExtension(file));
                canidatesToBatchImport.Add(canidateDir);
            }
        }

        List<string> directoriesToBatchImport = new();
        foreach (var dir in canidatesToBatchImport)
        {
            directoriesToBatchImport.AddRange(Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Where(ShouldImportFile).ToArray());
        }
        
        await default(ToWorld);

        var slot = Engine.Current.WorldManager.FocusedWorld.AddSlot("GTA Import");
        slot.PositionInFrontOfUser();
        BatchFolderImporter.BatchImport(slot, directoriesToBatchImport);
        
        // As GTA uses DirectX normal maps, we need to invert the green channel
        foreach (var staticTexture in slot.GetComponentsInChildren<StaticTexture2D>())
        {
            if (staticTexture.IsNormalMap)
            {
                staticTexture.ProcessPixels((color c) => new color(c.r, 1f - c.g, c.b, c.a));
            }
        }
    }

    private static void RunBlenderScript(string script, string arguments = "-b -P \"{0}\"")
    {
        var tempBlenderScript = Path.Combine(Path.GetTempPath(), Path.GetTempFileName() + ".py");
        File.WriteAllText(tempBlenderScript, script);
        var blenderArgs = string.Format(arguments, tempBlenderScript);
        blenderArgs = "--disable-autoexec " + blenderArgs;

        var process = new Process();
        process.StartInfo.FileName = BlenderInterface.Executable;
        process.StartInfo.Arguments = blenderArgs;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
        process.OutputDataReceived += OnOutput;
        process.Start();
        process.BeginOutputReadLine();
        process.WaitForExit();

        File.Delete(tempBlenderScript);
    }
    
    private static void GTAToGLB(string input, string outputDir, string outputName)
    {
        RunBlenderScript(@$"import bpy
bpy.ops.import_scene.gta(filepath = '{input}')
objs = bpy.data.objects
try:    
    objs.remove(objs['Cube'], do_unlink = True)
except:    
    pass  
bpy.ops.export_scene.gltf(filepath = '{outputDir}\\{outputName}.gltf')
");
    }

    private static void OnOutput(object sender, DataReceivedEventArgs e)
    {
        Msg(e.Data);
    }

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
            customFileExtensions.Contains(Path.GetExtension(file).ToLower());
    }
}
