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
    internal readonly static ModConfigurationKey<bool> importAsRawFiles =
         new("importAsRawFiles",
         "Import raw versions of GTA files into Resonite",
         () => false);
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
        Engine.Current.RunPostInit(() => AssetPatch());
    }

    private static void AssetPatch()
    {
        var aExt = Traverse.Create(typeof(AssetHelper)).Field<Dictionary<AssetClass, List<string>>>("associatedExtensions");
        aExt.Value[AssetClass.Special].Add("odr");
        aExt.Value[AssetClass.Special].Add("odd");
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
            if (config.GetValue(importAsRawFiles))
            {
                await default(ToWorld);
                var rawSlot = world.AddSlot();
                rawSlot.PositionInFrontOfUser();
                await UniversalImporter.ImportRawFile(rawSlot, file);
                await default(ToBackground);
                continue;     
            }

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
        BatchFolderImporter.BatchImport(slot, directoriesToBatchImport, config.GetValue(importAsRawFiles));
        
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
