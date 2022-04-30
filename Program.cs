using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.CommandLine;
using IniParser;
using IniParser.Model;
using Infinite_Coating_Tool.Json;

namespace Infinite_Coating_Tool
{
    class Program
    {
        public static string detailMapsLocation;
        public static string output;
        static int Main(string[] args)
        {
            // System.CommandLine setup
            var inOption = new Option<FileInfo>(
                "-i",
                "JSON file containing the armor coating info.");
            
            var nameOption = new Option<string>(
                "-n",
                getDefaultValue: () => null,
                description: "Armor coating name.");

            //var outOption = new Option<DirectoryInfo>(
            //    "-o",
            //    "Directory to place the result in.");

            var rootCommand = new RootCommand
            {
                inOption,
                nameOption,
            //    outOption
            };

            rootCommand.Description = "Converts an armor coating data JSON file from the Halo Waypoint API to a preset file usable in Blender.";

            rootCommand.SetHandler((FileInfo i, string n/*, DirectoryInfo o*/) =>
            {
                Fill(i,n/*,o*/);
            }, inOption, nameOption/*, outOption*/);

            return rootCommand.Invoke(args);
        }

        static void Fill(FileInfo input, string name/*, DirectoryInfo output*/)
        {
            // Load cached data
            var parser = new FileIniDataParser();
            IniData cache = parser.ReadFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources","cache.ini"));
            detailMapsLocation = cache["Paths"]["detailMaps"];
            output = cache["Paths"]["output"];

            // Fill in output if missing.
            // Not filling detail maps because it would be annoying to ask every time if you don't have them.
            if (output == "")
            {
                Console.WriteLine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources","cache.ini"));
                Console.WriteLine("Output location in cache.ini is empty. Please give a folder:");
                Console.Write("> ");
                output = Console.ReadLine();
                cache["Paths"]["output"] = output;
                parser.WriteFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources","cache.ini"), cache);
            }
            
            // Load outside files
            CoatingContainer coating = JsonSerializer.Deserialize<CoatingContainer>(input.OpenText().ReadToEnd());
            string pyScript = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources","template.py"));

            // Pre-Process the coating data -- swatch IDs are part of the swatch data rather than their identifiers,
            // which is a pain to reference. Dictionaries are simple to set up and we don't care about speed.
            Dictionary<string, Swatch> swatches = new Dictionary<string, Swatch>();
            foreach(Swatch s in coating.swatches)
            {
                swatches.Add(s.swatchId, s);
            }

            // Make a version of the script for region
            Dictionary<string,string> regionScripts = new Dictionary<string, string>();
            foreach(KeyValuePair<string,Region> l in coating.regionLayers)
            {
                string regionName = l.Key;

                string regionedPyScript = pyScript.Replace("REGIONNAME", regionName).Replace("COATINGNAME", name).Replace("DETAILMAPS", detailMapsLocation.Replace("\\","\\\\"));

                // Determine which layers of the shader to fill in. 8 is for campaign only, 7 is for standard MP,
                // 4 is for MP knees/elbows, and 1 is for MP visors.
                // Bits in mask are in reverse order to make comparison easier.
                switch(l.Value.material)
                {
                    case "cvw_7_layered":
                        regionScripts.Add(regionName, ReplaceEnums(l.Value, swatches, coating, regionedPyScript, 0b_0111_1111));
                        break;
                    case "cvw_4_layered":
                        regionScripts.Add(regionName, ReplaceEnums(l.Value, swatches, coating, regionedPyScript, 0b_0111_0001));
                        break;
                    case "cvw_1_layered":
                        regionScripts.Add(regionName, ReplaceEnums(l.Value, swatches, coating, regionedPyScript, 0b_0000_0001));
                        break;
                    case "cvw_7_layered_damage":
                        regionScripts.Add(regionName, ReplaceEnums(l.Value, swatches, coating, regionedPyScript, 0b_1111_1111));
                        break;
                    case "cvw_4_layered_damage":
                        regionScripts.Add(regionName, ReplaceEnums(l.Value, swatches, coating, regionedPyScript, 0b_1111_0001));
                        break;
                    case "cvw_1_layered_damage":
                        regionScripts.Add(regionName, ReplaceEnums(l.Value, swatches, coating, regionedPyScript, 0b_1000_0001));
                        break;
                    default:
                        throw new UnknownMaterialException($"\'{l.Value.material}\' is not a recognized material name.");
                }
            }

            if (!Directory.Exists(Path.Combine(output, PathFix.SanitizeName(name))))
                Directory.CreateDirectory(Path.Combine(output, PathFix.SanitizeName(name)));
            foreach(KeyValuePair<string,string> script in regionScripts)
            {
                File.WriteAllText(Path.Combine(output, PathFix.SanitizeName(name), PathFix.SanitizeName($"{name}_{script.Key}.py")), script.Value);
            }
        }

        static string ReplaceEnums(Region region, Dictionary<string, Swatch> swatches, CoatingContainer coating, string pyScript, byte mask)
        {
            int currentLayer=0;
            for (int i=0; i<8; i++)
            {
                // Load groups, since those are used to enable/disable edge wear
                var parser = new FileIniDataParser();
                IniData groups = parser.ReadFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources","groups.ini"));
                
                string id = "";
                // Only apply swatches to masked layers (need to figure out if unmasked ones use layer 0, default vals, or grime swatch)
                if (((byte)Math.Pow(2,i) & mask) != 0)
                {
                    if (region.layers.Length > currentLayer)
                        id = region.layers[currentLayer].swatch;
                    currentLayer++;
                }
                // If no swatch is specified, assume same swatch as layer 0
                if (id=="")
                    id = region.layers[0].swatch;
                Swatch swatch = swatches[id];

                // Get edge wear bool as int factor, default to 1 for unknown groups
                int edgeWear = 1;
                if (groups["scratches"].ContainsKey(swatch.groupName))
                    edgeWear = int.Parse(groups["scratches"][swatch.groupName]);
                else
                    Console.WriteLine($"Unknown group \'{swatch.groupName}\' in swatch.");

                // Fill in the script values
                pyScript = pyScript.Replace($"GRIMEAMOUNT", coating.grimeAmount.ToString());
                pyScript = pyScript.Replace($"SCRATCHAMOUNT", (coating.scratchAmount*edgeWear).ToString());
                pyScript = pyScript.Replace($"UNIEMITAMOUNT", coating.emissiveAmount.ToString());

                pyScript = pyScript.Replace($"GROUPNAME_{i+1}", swatch.groupName);

                pyScript = pyScript.Replace($"IOR_{i+1}", swatch.ior.ToString());
                
                pyScript = pyScript.Replace($"SCALEX_{i+1}", swatch.normalTextureTransform[0].ToString());
                pyScript = pyScript.Replace($"SCALEY_{i+1}", swatch.normalTextureTransform[1].ToString());

                pyScript = pyScript.Replace($"ROUGHNESS_{i+1}", swatch.roughness.ToString());
                pyScript = pyScript.Replace($"ROUGHNESSB_{i+1}", swatch.roughnessBlack.ToString());
                pyScript = pyScript.Replace($"ROUGHNESSW_{i+1}", swatch.roughnessWhite.ToString());

                pyScript = pyScript.Replace($"SCRATCHMETAL_{i+1}", swatch.scratchMetallic.ToString());
                pyScript = pyScript.Replace($"SCRATCHROUGH_{i+1}", swatch.scratchRoughness.ToString());
                pyScript = pyScript.Replace($"SCRATCHALBTINT_{i+1}", swatch.scratchAlbedoTint.ToString());
                pyScript = pyScript.Replace($"SCRATCHBRIGHT_{i+1}", swatch.scratchBrightness.ToString());
                pyScript = pyScript.Replace($"SCRATCHIOR_{i+1}", swatch.scratchIor.ToString());

                pyScript = pyScript.Replace($"METAL_{i+1}", swatch.metallic.ToString());
                pyScript = pyScript.Replace($"EMITAMOUNT_{i+1}", swatch.emissiveAmount.ToString());
                pyScript = pyScript.Replace($"EMITINTENSE_{i+1}", swatch.emissiveIntensity.ToString());

                pyScript = pyScript.Replace($"TOPCOLOR0_{i+1}", swatch.colorVariant.topColor[0].ToString());
                pyScript = pyScript.Replace($"TOPCOLOR1_{i+1}", swatch.colorVariant.topColor[1].ToString());
                pyScript = pyScript.Replace($"TOPCOLOR2_{i+1}", swatch.colorVariant.topColor[2].ToString());

                pyScript = pyScript.Replace($"MIDCOLOR0_{i+1}", swatch.colorVariant.midColor[0].ToString());
                pyScript = pyScript.Replace($"MIDCOLOR1_{i+1}", swatch.colorVariant.midColor[1].ToString());
                pyScript = pyScript.Replace($"MIDCOLOR2_{i+1}", swatch.colorVariant.midColor[2].ToString());

                pyScript = pyScript.Replace($"BOTCOLOR0_{i+1}", swatch.colorVariant.botColor[0].ToString());
                pyScript = pyScript.Replace($"BOTCOLOR1_{i+1}", swatch.colorVariant.botColor[1].ToString());
                pyScript = pyScript.Replace($"BOTCOLOR2_{i+1}", swatch.colorVariant.botColor[2].ToString());
                
                pyScript = pyScript.Replace($"SCRCOLOR0_{i+1}", swatch.scratchColor[0].ToString());
                pyScript = pyScript.Replace($"SCRCOLOR1_{i+1}", swatch.scratchColor[1].ToString());
                pyScript = pyScript.Replace($"SCRCOLOR2_{i+1}", swatch.scratchColor[2].ToString());

                pyScript = pyScript.Replace($"GRADMASK_{i+1}", swatch.colorGradientMap.Replace("\\","\\\\"));
                pyScript = pyScript.Replace($"NORMAL_{i+1}", swatch.normalPath.Replace("\\","\\\\"));
                
            }
            return pyScript;
        }
    }

    public class UnknownMaterialException : Exception
    {
        public UnknownMaterialException()
        {
        }

        public UnknownMaterialException(string message)
            : base(message)
        {
        }

        public UnknownMaterialException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    public class PathFix
    {
        public static string SanitizeName( string name )
        {
            string invalidChars = System.Text.RegularExpressions.Regex.Escape( new string( System.IO.Path.GetInvalidFileNameChars() ) );
            string invalidRegStr = string.Format( @"([{0}]*\.+$)|([{0}]+)", invalidChars );

            return System.Text.RegularExpressions.Regex.Replace( name, invalidRegStr, "_" );
        }
    }
}
