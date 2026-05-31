using System.Text.Json;
using ResourcePackConvert.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ResourcePackConvert.Core.Services;

public class PbrConverter
{
    private readonly PbrStats _conversionStats = new();

    private readonly Dictionary<int, (string Name, float[] F0)> _metalDefinitions = new()
    {
        [230] = ("iron", [0.56f, 0.57f, 0.58f]),
        [231] = ("gold", [1.00f, 0.78f, 0.34f]),
        [232] = ("aluminum", [0.91f, 0.92f, 0.92f]),
        [233] = ("chrome", [0.55f, 0.56f, 0.56f]),
        [234] = ("copper", [0.95f, 0.64f, 0.54f]),
        [235] = ("lead", [0.63f, 0.63f, 0.66f]),
        [236] = ("platinum", [0.67f, 0.69f, 0.66f]),
        [237] = ("silver", [0.95f, 0.93f, 0.88f])
    };

    public Dictionary<string, Dictionary<string, string>> DetectPbrTextures(string textureDir)
    {
        var pbrSets = new Dictionary<string, Dictionary<string, string>>();

        var extensions = new[] { "*.png", "*.jpg", "*.jpeg", "*.tga" };
        var textureFiles = extensions.SelectMany(ext =>
            Directory.GetFiles(textureDir, ext, SearchOption.AllDirectories));

        foreach (var textureFile in textureFiles)
        {
            var name = Path.GetFileNameWithoutExtension(textureFile);

            if (name.EndsWith("_s"))
            {
                var baseName = name[..^2];
                if (!pbrSets.ContainsKey(baseName))
                    pbrSets[baseName] = new Dictionary<string, string>();
                pbrSets[baseName]["specular"] = textureFile;
            }
            else if (name.EndsWith("_n"))
            {
                var baseName = name[..^2];
                if (!pbrSets.ContainsKey(baseName))
                    pbrSets[baseName] = new Dictionary<string, string>();
                pbrSets[baseName]["normal"] = textureFile;
            }
            else if (!name.EndsWith("_s") && !name.EndsWith("_n") &&
                     !name.EndsWith("_e") && !name.EndsWith("_r") &&
                     !name.EndsWith("_m") && !name.EndsWith("_mer") &&
                     !name.EndsWith("_normal"))
            {
                if (!pbrSets.ContainsKey(name))
                    pbrSets[name] = new Dictionary<string, string>();
                pbrSets[name]["diffuse"] = textureFile;
            }
        }

        // Keep only sets with specular or normal maps
        pbrSets = pbrSets.Where(kvp =>
            kvp.Value.ContainsKey("specular") || kvp.Value.ContainsKey("normal"))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Console.WriteLine($"[INFO] Detected {pbrSets.Count} PBR texture sets");
        foreach (var (setName, components) in pbrSets)
        {
            Console.WriteLine($"[DEBUG]   {setName}: {string.Join(", ", components.Keys)}");
        }

        return pbrSets;
    }

    public bool ConvertSpecularToMer(string specularPath, string merPath)
    {
        try
        {
            using var specularImg = Image.Load<Rgba32>(specularPath);

            var width = specularImg.Width;
            var height = specularImg.Height;

            using var merImg = new Image<Rgb24>(width, height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = specularImg[x, y];

                    // Extract channels
                    float smoothness = pixel.R / 255f;
                    float f0Metal = pixel.G;
                    // float porositySss = pixel.B;  // unused for MER
                    float emission = pixel.A;

                    // Roughness from smoothness (perceptual -> linear)
                    float roughnessLinear = MathF.Pow(1f - smoothness, 2f);
                    byte roughnessBedrock = (byte)(roughnessLinear * 255f);

                    // Metalness
                    byte metalness = 0;
                    if (f0Metal >= 230)
                    {
                        metalness = 255; // Full metalness for predefined metals
                    }
                    else if (f0Metal > 10)
                    {
                        metalness = (byte)f0Metal; // Some metalness for higher F0 values
                    }

                    // Emission (inverted: 255 - alpha for non-emitting)
                    byte emissionValue = emission < 255 ? (byte)emission : (byte)0;

                    merImg[x, y] = new Rgb24(metalness, emissionValue, roughnessBedrock);
                }
            }

            var merDir = Path.GetDirectoryName(merPath);
            if (!string.IsNullOrEmpty(merDir))
                Directory.CreateDirectory(merDir);

            merImg.Save(merPath);

            _conversionStats.SpecularConverted++;
            _conversionStats.MerGenerated++;

            Console.WriteLine($"[DEBUG] Converted specular to MER: {Path.GetFileName(specularPath)} -> {Path.GetFileName(merPath)}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to convert specular map {specularPath}: {ex.Message}");
            _conversionStats.Errors++;
            return false;
        }
    }

    public bool ConvertNormalMap(string normalPath, string outputPath)
    {
        try
        {
            using var normalImg = Image.Load<Rgba32>(normalPath);

            var width = normalImg.Width;
            var height = normalImg.Height;

            using var bedrockNormal = new Image<Rgb24>(width, height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = normalImg[x, y];

                    // Extract normal XY
                    byte normalX = pixel.R;
                    byte normalY = pixel.G;

                    // Reconstruct Z from XY
                    float nx = (normalX / 255f) * 2f - 1f;
                    float ny = (normalY / 255f) * 2f - 1f;

                    float nzSquared = 1f - (nx * nx + ny * ny);
                    nzSquared = MathF.Max(0f, nzSquared);
                    float nz = MathF.Sqrt(nzSquared);

                    byte normalZ = (byte)((nz + 1f) / 2f * 255f);

                    bedrockNormal[x, y] = new Rgb24(normalX, normalY, normalZ);
                }
            }

            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
                Directory.CreateDirectory(outputDir);

            bedrockNormal.Save(outputPath);

            _conversionStats.NormalConverted++;

            Console.WriteLine($"[DEBUG] Converted normal map: {Path.GetFileName(normalPath)} -> {Path.GetFileName(outputPath)}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to convert normal map {normalPath}: {ex.Message}");
            _conversionStats.Errors++;
            return false;
        }
    }

    public bool CreateTextureSetJson(string textureName, string outputDir,
        bool hasMer = false, bool hasNormal = false)
    {
        try
        {
            var textureSet = new Dictionary<string, object>
            {
                ["format_version"] = "1.16.100",
                ["minecraft:texture_set"] = new Dictionary<string, object>
                {
                    ["color"] = textureName
                }
            };

            var ts = (Dictionary<string, object>)textureSet["minecraft:texture_set"];

            if (hasMer)
                ts["metalness_emissive_roughness"] = $"{textureName}_mer";

            if (hasNormal)
                ts["normal"] = $"{textureName}_normal";

            string blocksDir;
            if (Path.GetFileName(outputDir) == "blocks")
            {
                blocksDir = outputDir;
            }
            else
            {
                var texturesDir = Path.GetDirectoryName(Path.GetDirectoryName(outputDir));
                blocksDir = texturesDir != null
                    ? Path.Combine(texturesDir, "blocks")
                    : Path.Combine(outputDir, "blocks");
            }

            Directory.CreateDirectory(blocksDir);

            var jsonPath = Path.Combine(blocksDir, $"{textureName}.texture_set.json");
            var json = JsonSerializer.Serialize(textureSet, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(jsonPath, json);

            _conversionStats.TextureSetsCreated++;

            Console.WriteLine($"[DEBUG] Created texture set JSON: {Path.GetFileName(jsonPath)}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to create texture set JSON for {textureName}: {ex.Message}");
            _conversionStats.Errors++;
            return false;
        }
    }

    public PbrStats ConvertPbrTextures(string javaTexturesDir, string bedrockTexturesDir,
        Dictionary<string, string>? textureMappings = null)
    {
        Console.WriteLine("[INFO] Starting PBR texture conversion...");

        var pbrSets = DetectPbrTextures(javaTexturesDir);

        if (pbrSets.Count == 0)
        {
            Console.WriteLine("[INFO] No PBR textures detected");
            return _conversionStats;
        }

        Console.WriteLine($"[INFO] Converting {pbrSets.Count} PBR texture sets...");

        foreach (var (setName, components) in pbrSets)
        {
            Console.WriteLine($"[DEBUG] Processing PBR set: {setName}");

            var diffuseName = $"{setName}.png";
            string bedrockBaseName;

            if (textureMappings != null && textureMappings.TryGetValue(diffuseName, out var mappedName))
            {
                bedrockBaseName = mappedName
                    .Replace(".png", "")
                    .Replace(".tga", "");
            }
            else
            {
                bedrockBaseName = setName;
            }

            var hasMer = false;
            var hasNormal = false;

            if (components.TryGetValue("specular", out var specularPath))
            {
                var merFilename = $"{bedrockBaseName}_mer.png";
                var merPath = Path.Combine(bedrockTexturesDir, "blocks", merFilename);

                if (ConvertSpecularToMer(specularPath, merPath))
                    hasMer = true;
            }

            if (components.TryGetValue("normal", out var normalPath))
            {
                var normalFilename = $"{bedrockBaseName}_normal.png";
                var normalOutPath = Path.Combine(bedrockTexturesDir, "blocks", normalFilename);

                if (ConvertNormalMap(normalPath, normalOutPath))
                    hasNormal = true;
            }

            if (hasMer || hasNormal)
            {
                var blocksDir = Path.Combine(bedrockTexturesDir, "blocks");
                CreateTextureSetJson(bedrockBaseName, blocksDir, hasMer, hasNormal);
            }
        }

        Console.WriteLine("[INFO] PBR conversion completed:");
        Console.WriteLine($"[INFO]   Specular maps converted: {_conversionStats.SpecularConverted}");
        Console.WriteLine($"[INFO]   Normal maps converted: {_conversionStats.NormalConverted}");
        Console.WriteLine($"[INFO]   MER maps generated: {_conversionStats.MerGenerated}");
        Console.WriteLine($"[INFO]   Texture set JSONs created: {_conversionStats.TextureSetsCreated}");
        Console.WriteLine($"[INFO]   Errors: {_conversionStats.Errors}");

        return _conversionStats;
    }

    public Dictionary<string, object> GetConversionReport()
    {
        return new Dictionary<string, object>
        {
            ["stats"] = _conversionStats,
            ["supported_features"] = new Dictionary<string, bool>
            {
                ["Specular to MER"] = true,
                ["Normal map conversion"] = true,
                ["Metalness detection"] = true,
                ["Emission mapping"] = true,
                ["Roughness conversion"] = true,
                ["Texture set JSON"] = true,
                ["Individual M/E/R maps"] = false
            }
        };
    }

}
