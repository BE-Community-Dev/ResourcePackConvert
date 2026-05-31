using System.Reflection;
using System.Text.Json;
using ResourcePackConvert.Core.Models;

namespace ResourcePackConvert.Core.Services;

public class ResourcePackConvertConverter
{
    private readonly string _mappingsDir;
    private readonly MappingLoader _mappingLoader;
    private readonly PackManager _packManager;
    private readonly TextureConverter _textureConverter;
    private readonly BedrockStructureGenerator _bedrockGenerator;
    private readonly PbrConverter _pbrConverter;

    private Dictionary<string, string> _textureMappings;

    /// <summary>
    /// Resolves a data directory path relative to the assembly location,
    /// falling back to the working-directory-relative path.
    /// </summary>
    internal static string ResolveDataPath(string relativePath)
    {
        var assemblyDir = AppContext.BaseDirectory;
        var assemblyRelative = Path.Combine(assemblyDir, relativePath);
        if (Directory.Exists(assemblyRelative))
            return assemblyRelative;

        // Fall back: try working directory
        if (Directory.Exists(relativePath))
            return relativePath;

        // Return assembly-relative path anyway (it will fail with a clear message later)
        return assemblyRelative;
    }

    public ResourcePackConvertConverter(string mappingsDir = "mappings")
    {
        _mappingsDir = ResolveDataPath(mappingsDir);

        _mappingLoader = new MappingLoader(_mappingsDir);
        _packManager = new PackManager();
        _textureConverter = new TextureConverter(_mappingsDir);
        _bedrockGenerator = new BedrockStructureGenerator();
        _pbrConverter = new PbrConverter();

        _textureMappings = _mappingLoader.LoadAllMappings();
        Console.WriteLine($"[INFO] Initialized converter with {_textureMappings.Count} texture mappings");
    }

    public bool ConvertResourcePack(string inputPath, string outputPath,
        string? packName = null, string? packDescription = null,
        bool validateInput = true, bool enablePbr = true)
    {
        try
        {
            Console.WriteLine($"[INFO] Starting conversion of {inputPath}");

            Console.WriteLine("[INFO] Validating input pack...");
            if (validateInput)
            {
                if (!ValidateInputPack(inputPath))
                    return false;
            }

            Console.WriteLine("[INFO] Preparing workspace...");
            var tempDir = _packManager.CreateTempDirectory("ResourcePackConvert_conversion");
            var javaTemp = Path.Combine(tempDir, "java_extracted");
            var bedrockTemp = Path.Combine(tempDir, "bedrock_build");

            Directory.CreateDirectory(javaTemp);
            Directory.CreateDirectory(bedrockTemp);

            Console.WriteLine("[INFO] Extracting Java pack...");
            var minecraftDir = _packManager.ExtractJavaPack(inputPath, javaTemp);
            if (minecraftDir == null)
                return false;

            var packInfo = _packManager.GetPackInfo(inputPath);
            packName ??= packInfo.Name;
            packDescription ??= packInfo.Description;

            Console.WriteLine($"[INFO] Converting pack: {packName}");
            Console.WriteLine($"[INFO] Original pack has {packInfo.TextureCount} textures in categories: {string.Join(", ", packInfo.Categories)}");

            Console.WriteLine("[INFO] Creating Bedrock structure...");
            _bedrockGenerator.CreateBedrockStructure(bedrockTemp);
            if (!_bedrockGenerator.GenerateManifest(bedrockTemp, packName, packDescription, enablePbr: enablePbr))
            {
                Console.WriteLine("[ERROR] Failed to generate manifest.json");
                return false;
            }

            Console.WriteLine("[INFO] Converting textures...");
            var javaTexturesDir = Path.Combine(minecraftDir, "textures");
            var bedrockTexturesDir = Path.Combine(bedrockTemp, "textures");

            var conversionStats = _textureConverter.ConvertTextures(javaTexturesDir, bedrockTexturesDir);
            LogConversionStats(conversionStats);

            PbrStats? pbrStats = null;
            if (enablePbr)
            {
                Console.WriteLine("[INFO] Processing PBR textures...");
            }
            else
            {
                Console.WriteLine("[INFO] Skipping PBR conversion...");
            }

            if (enablePbr)
            {
                pbrStats = _pbrConverter.ConvertPbrTextures(javaTexturesDir, bedrockTexturesDir, _textureMappings);
                LogPbrStats(pbrStats);
            }

            Console.WriteLine("[INFO] Validating missing mappings...");
            ValidateMissingMappings(javaTexturesDir);

            Console.WriteLine("[INFO] Copying required files...");

            _bedrockGenerator.GenerateTerrainTextureJson(bedrockTemp);
            _bedrockGenerator.GenerateItemTextureJson(bedrockTemp);
            _bedrockGenerator.GenerateBlocksJson(bedrockTemp);

            var javaLangDir = Path.Combine(minecraftDir, "lang");
            _bedrockGenerator.GenerateLanguageFiles(bedrockTemp, javaLangDir);

            var assetStats = _packManager.CopyOtherAssets(minecraftDir, bedrockTemp);
            LogAssetStats(assetStats);

            Console.WriteLine("[INFO] Creating .mcpack file...");
            if (!_packManager.CreateMcpack(bedrockTemp, outputPath))
            {
                Console.WriteLine("[ERROR] Failed to create .mcpack file");
                return false;
            }

            GenerateConversionReport(conversionStats, assetStats, pbrStats, outputPath, enablePbr);

            Console.WriteLine("[INFO] Conversion completed successfully!");
            Console.WriteLine($"[INFO] Output saved to: {outputPath}");
            if (enablePbr)
                Console.WriteLine("[INFO] PBR textures converted - pack supports RTX/ray tracing features");

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Conversion failed: {ex.Message}");
            return false;
        }
        finally
        {
            _packManager.CleanupTempDirectories();
        }
    }

    private bool ValidateInputPack(string inputPath)
    {
        var validationResult = _packManager.ValidateJavaPack(inputPath);

        if (!validationResult.Valid)
        {
            Console.WriteLine("[ERROR] Input pack validation failed:");
            foreach (var error in validationResult.Errors)
                Console.WriteLine($"[ERROR]   - {error}");
            return false;
        }

        Console.WriteLine("[INFO] Input pack validation passed");
        Console.WriteLine($"[INFO] Pack has {validationResult.TextureCategories.Count} texture categories: {string.Join(", ", validationResult.TextureCategories)}");
        return true;
    }

    private void LogConversionStats(ConversionStats stats)
    {
        Console.WriteLine("[INFO] Texture conversion completed:");
        Console.WriteLine($"[INFO]   Total files processed: {stats.Total}");
        Console.WriteLine($"[INFO]   Successfully converted: {stats.Converted}");
        Console.WriteLine($"[INFO]   Skipped (already exist): {stats.Skipped}");
        Console.WriteLine($"[INFO]   Missing mappings: {stats.Missing}");
        Console.WriteLine($"[INFO]   Errors: {stats.Errors}");

        if (stats.Missing > 0)
            Console.WriteLine("[WARNING] Some textures have missing mappings - check missing_mappings.json for details");
    }

    private void LogPbrStats(PbrStats stats)
    {
        Console.WriteLine("[INFO] PBR conversion completed:");
        Console.WriteLine($"[INFO]   Specular maps converted: {stats.SpecularConverted}");
        Console.WriteLine($"[INFO]   Normal maps converted: {stats.NormalConverted}");
        Console.WriteLine($"[INFO]   MER maps generated: {stats.MerGenerated}");
        Console.WriteLine($"[INFO]   Texture set JSONs created: {stats.TextureSetsCreated}");
        Console.WriteLine($"[INFO]   PBR errors: {stats.Errors}");
    }

    private void LogAssetStats(AssetStats stats)
    {
        if (stats.Total > 0)
        {
            Console.WriteLine("[INFO] Other assets copied:");
            foreach (var (assetType, count) in stats)
            {
                if (count > 0)
                    Console.WriteLine($"[INFO]   {assetType}: {count} files");
            }
        }
    }

    private void GenerateConversionReport(ConversionStats conversionStats, AssetStats assetStats,
        PbrStats? pbrStats, string outputPath, bool pbrEnabled)
    {
        try
        {
            var outputDir = Path.GetDirectoryName(outputPath);
            var reportPath = Path.Combine(outputDir ?? ".", "conversion_report.txt");

            using var writer = new StreamWriter(reportPath);

            writer.WriteLine("=== ResourcePackConvert Conversion Report ===\n");
            writer.WriteLine($"Output pack: {outputPath}");
            writer.WriteLine($"PBR enabled: {pbrEnabled}");
            writer.WriteLine($"Total mappings available: {_textureMappings.Count}\n");

            writer.WriteLine("Texture Conversion Statistics:");
            writer.WriteLine($"  converted: {conversionStats.Converted}");
            writer.WriteLine($"  skipped: {conversionStats.Skipped}");
            writer.WriteLine($"  missing: {conversionStats.Missing}");
            writer.WriteLine($"  errors: {conversionStats.Errors}");

            if (pbrEnabled && pbrStats != null)
            {
                writer.WriteLine("\nPBR Conversion Statistics:");
                writer.WriteLine($"  specular_converted: {pbrStats.SpecularConverted}");
                writer.WriteLine($"  normal_converted: {pbrStats.NormalConverted}");
                writer.WriteLine($"  mer_generated: {pbrStats.MerGenerated}");
                writer.WriteLine($"  texture_sets_created: {pbrStats.TextureSetsCreated}");
                writer.WriteLine($"  errors: {pbrStats.Errors}");
            }

            writer.WriteLine("\nAsset Copy Statistics:");
            foreach (var (assetType, count) in assetStats)
            {
                if (count > 0)
                    writer.WriteLine($"  {assetType}: {count}");
            }

            var conversionReport = _textureConverter.GetConversionReport();
            writer.WriteLine("\nDetailed Conversion Info:");
            writer.WriteLine($"  Files with missing mappings: {conversionReport["missing_mappings"]}");

            var missingList = (List<string>)conversionReport["missing_files_list"];
            if (missingList.Count > 0)
            {
                writer.WriteLine("\nTextures without mappings (first 20):");
                foreach (var missingFile in missingList.Take(20))
                    writer.WriteLine($"  - {missingFile}");
            }

            writer.WriteLine("\nMapping categories loaded:");
            foreach (var category in _mappingLoader.GetCategories())
            {
                var categoryMappings = _mappingLoader.GetMappingByCategory(category);
                writer.WriteLine($"  {category}: {categoryMappings.Count} mappings");
            }

            if (pbrEnabled)
            {
                var pbrReport = _pbrConverter.GetConversionReport();
                var features = (Dictionary<string, bool>)pbrReport["supported_features"];
                writer.WriteLine("\nPBR Support Features:");
                foreach (var (feature, supported) in features)
                    writer.WriteLine($"  {feature}: {(supported ? "Yes" : "No")}");
            }

            Console.WriteLine($"[INFO] Conversion report saved to: {reportPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARNING] Failed to generate conversion report: {ex.Message}");
        }
    }

    public Dictionary<string, object> GetAvailableMappingsInfo()
    {
        var categories = _mappingLoader.GetCategories();
        var mappingInfo = new Dictionary<string, object>
        {
            ["total_mappings"] = _textureMappings.Count,
            ["category_count"] = categories.Count
        };

        var categoryDetails = new Dictionary<string, object>();
        foreach (var category in categories)
        {
            var categoryMappings = _mappingLoader.GetMappingByCategory(category);
            categoryDetails[category] = new Dictionary<string, object>
            {
                ["count"] = categoryMappings.Count,
                ["sample_mappings"] = categoryMappings.Take(3)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            };
        }

        mappingInfo["categories"] = categoryDetails;
        return mappingInfo;
    }

    public Dictionary<string, object> ValidateMappings()
    {
        return _mappingLoader.ValidateMappings();
    }

    private void ValidateMissingMappings(string javaTexturesDir)
    {
        try
        {
            var missingFile = "missing_mappings.json";
            if (!File.Exists(missingFile))
            {
                Console.WriteLine("[INFO] No missing_mappings.json file found");
                return;
            }

            var json = File.ReadAllText(missingFile);
            using var doc = JsonDocument.Parse(json);
            var missingTextures = new List<string>();

            // Handle both old array format and new dict format
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                missingTextures = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            else if (doc.RootElement.TryGetProperty("mappings", out var mappingsEl))
            {
                foreach (var prop in mappingsEl.EnumerateObject())
                    missingTextures.Add(prop.Name);
            }

            var codeIssues = new List<string>();
            var actuallyMissing = new List<string>();

            foreach (var textureName in missingTextures)
            {
                var possiblePaths = new[]
                {
                    Path.Combine(javaTexturesDir, $"{textureName}.png"),
                    Path.Combine(javaTexturesDir, "block", $"{textureName}.png"),
                    Path.Combine(javaTexturesDir, "item", $"{textureName}.png"),
                    Path.Combine(javaTexturesDir, "entity", $"{textureName}.png"),
                    Path.Combine(javaTexturesDir, "models", $"{textureName}.png"),
                    Path.Combine(javaTexturesDir, "gui", $"{textureName}.png"),
                };

                var recursiveMatches = Directory.GetFiles(javaTexturesDir, $"{textureName}.png", SearchOption.AllDirectories);

                var textureExists = possiblePaths.Any(File.Exists) || recursiveMatches.Length > 0;

                if (textureExists)
                {
                    codeIssues.Add(textureName);
                    if (recursiveMatches.Length > 0)
                    {
                        var foundPath = Path.GetRelativePath(javaTexturesDir, recursiveMatches[0]);
                        Console.WriteLine($"[WARNING] Texture '{textureName}' found at '{foundPath}' but not converted - possible mapping issue");
                    }
                    else
                    {
                        Console.WriteLine($"[WARNING] Texture '{textureName}' exists in pack but marked as missing - possible mapping issue");
                    }
                }
                else
                {
                    actuallyMissing.Add(textureName);
                }
            }

            if (codeIssues.Count > 0)
            {
                Console.WriteLine($"[WARNING] Found {codeIssues.Count} textures that exist but weren't converted:");
                foreach (var texture in codeIssues.Take(5))
                    Console.WriteLine($"[WARNING]   - {texture}");
                if (codeIssues.Count > 5)
                    Console.WriteLine($"[WARNING]   ... and {codeIssues.Count - 5} more");
                Console.WriteLine("[WARNING] These may indicate mapping issues that need to be fixed");
            }

            if (actuallyMissing.Count > 0)
                Console.WriteLine($"[INFO] Confirmed {actuallyMissing.Count} textures are genuinely missing from the pack");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to validate missing mappings: {ex.Message}");
        }
    }
}
