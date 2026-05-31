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
    /// Optional progress callback — reports conversion stages and statistics.
    /// Subscribe via <c>new Progress&lt;string&gt;(msg => ...)</c>.
    /// </summary>
    private readonly IProgress<string>? _progress;

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

    private void ReportProgress(string message)
    {
        _progress?.Report(message);
    }

    /// <summary>
    /// Creates a converter with optional progress reporting.
    /// </summary>
    /// <param name="mappingsDir">Folder name for mapping files (embedded or on-disk).</param>
    /// <param name="progress">Optional <see cref="IProgress{T}"/> callback.</param>
    public ResourcePackConvertConverter(string mappingsDir = "Mappings", IProgress<string>? progress = null)
    {
        _progress = progress;
        _mappingsDir = ResolveDataPath(mappingsDir);

        _mappingLoader = new MappingLoader(_mappingsDir);
        _packManager = new PackManager();
        _textureConverter = new TextureConverter(_mappingsDir);
        _bedrockGenerator = new BedrockStructureGenerator();
        _pbrConverter = new PbrConverter();

        _textureMappings = _mappingLoader.LoadAllMappings();
        ReportProgress($"[INFO] Initialized converter with {_textureMappings.Count} texture mappings");
    }

    public bool ConvertResourcePack(string inputPath, string outputPath,
        string? packName = null, string? packDescription = null,
        bool validateInput = true, bool enablePbr = true)
    {
        try
        {
            ReportProgress($"[INFO] Starting conversion of {inputPath}");

            ReportProgress("[INFO] Validating input pack...");
            if (validateInput)
            {
                if (!ValidateInputPack(inputPath))
                    return false;
            }

            ReportProgress("[INFO] Preparing workspace...");
            var tempDir = _packManager.CreateTempDirectory("ResourcePackConvert_conversion");
            var javaTemp = Path.Combine(tempDir, "java_extracted");
            var bedrockTemp = Path.Combine(tempDir, "bedrock_build");

            Directory.CreateDirectory(javaTemp);
            Directory.CreateDirectory(bedrockTemp);

            ReportProgress("[INFO] Extracting Java pack...");
            var minecraftDir = _packManager.ExtractJavaPack(inputPath, javaTemp);
            if (minecraftDir == null)
                return false;

            var packInfo = _packManager.GetPackInfo(inputPath);
            packName ??= packInfo.Name;
            packDescription ??= packInfo.Description;

            ReportProgress($"[INFO] Converting pack: {packName}");
            ReportProgress($"[INFO] Original pack has {packInfo.TextureCount} textures in categories: {string.Join(", ", packInfo.Categories)}");

            ReportProgress("[INFO] Creating Bedrock structure...");
            _bedrockGenerator.CreateBedrockStructure(bedrockTemp);
            if (!_bedrockGenerator.GenerateManifest(bedrockTemp, packName, packDescription, enablePbr: enablePbr))
            {
                ReportProgress("[ERROR] Failed to generate manifest.json");
                return false;
            }

            ReportProgress("[INFO] Converting textures...");
            var javaTexturesDir = Path.Combine(minecraftDir, "textures");
            var bedrockTexturesDir = Path.Combine(bedrockTemp, "textures");

            var conversionStats = _textureConverter.ConvertTextures(javaTexturesDir, bedrockTexturesDir);
            LogConversionStats(conversionStats);

            PbrStats? pbrStats = null;
            if (enablePbr)
            {
                ReportProgress("[INFO] Processing PBR textures...");
            }
            else
            {
                ReportProgress("[INFO] Skipping PBR conversion...");
            }

            if (enablePbr)
            {
                pbrStats = _pbrConverter.ConvertPbrTextures(javaTexturesDir, bedrockTexturesDir, _textureMappings);
                LogPbrStats(pbrStats);
            }

            ReportProgress("[INFO] Validating missing mappings...");
            ValidateMissingMappings(javaTexturesDir);

            ReportProgress("[INFO] Copying required files...");

            _bedrockGenerator.GenerateTerrainTextureJson(bedrockTemp);
            _bedrockGenerator.GenerateItemTextureJson(bedrockTemp);
            _bedrockGenerator.GenerateBlocksJson(bedrockTemp);

            var javaLangDir = Path.Combine(minecraftDir, "lang");
            _bedrockGenerator.GenerateLanguageFiles(bedrockTemp, javaLangDir);

            var assetStats = _packManager.CopyOtherAssets(minecraftDir, bedrockTemp);
            LogAssetStats(assetStats);

            ReportProgress("[INFO] Creating .mcpack file...");
            if (!_packManager.CreateMcpack(bedrockTemp, outputPath))
            {
                ReportProgress("[ERROR] Failed to create .mcpack file");
                return false;
            }

            GenerateConversionReport(conversionStats, assetStats, pbrStats, outputPath, enablePbr);

            ReportProgress("[INFO] Conversion completed successfully!");
            ReportProgress($"[INFO] Output saved to: {outputPath}");
            if (enablePbr)
                ReportProgress("[INFO] PBR textures converted - pack supports RTX/ray tracing features");

            return true;
        }
        catch (Exception ex)
        {
            ReportProgress($"[ERROR] Conversion failed: {ex.Message}");
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
            ReportProgress("[ERROR] Input pack validation failed:");
            foreach (var error in validationResult.Errors)
                ReportProgress($"[ERROR]   - {error}");
            return false;
        }

        ReportProgress("[INFO] Input pack validation passed");
        ReportProgress($"[INFO] Pack has {validationResult.TextureCategories.Count} texture categories: {string.Join(", ", validationResult.TextureCategories)}");
        return true;
    }

    private void LogConversionStats(ConversionStats stats)
    {
        ReportProgress("[INFO] Texture conversion completed:");
        ReportProgress($"[INFO]   Total files processed: {stats.Total}");
        ReportProgress($"[INFO]   Successfully converted: {stats.Converted}");
        ReportProgress($"[INFO]   Skipped (already exist): {stats.Skipped}");
        ReportProgress($"[INFO]   Missing mappings: {stats.Missing}");
        ReportProgress($"[INFO]   Errors: {stats.Errors}");

        if (stats.Missing > 0)
            ReportProgress("[WARNING] Some textures have missing mappings - check missing_mappings.json for details");
    }

    private void LogPbrStats(PbrStats stats)
    {
        ReportProgress("[INFO] PBR conversion completed:");
        ReportProgress($"[INFO]   Specular maps converted: {stats.SpecularConverted}");
        ReportProgress($"[INFO]   Normal maps converted: {stats.NormalConverted}");
        ReportProgress($"[INFO]   MER maps generated: {stats.MerGenerated}");
        ReportProgress($"[INFO]   Texture set JSONs created: {stats.TextureSetsCreated}");
        ReportProgress($"[INFO]   PBR errors: {stats.Errors}");
    }

    private void LogAssetStats(AssetStats stats)
    {
        if (stats.Total > 0)
        {
            ReportProgress("[INFO] Other assets copied:");
            foreach (var (assetType, count) in stats)
            {
                if (count > 0)
                    ReportProgress($"[INFO]   {assetType}: {count} files");
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

            ReportProgress($"[INFO] Conversion report saved to: {reportPath}");
        }
        catch (Exception ex)
        {
            ReportProgress($"[WARNING] Failed to generate conversion report: {ex.Message}");
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
                ReportProgress("[INFO] No missing_mappings.json file found");
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
                        ReportProgress($"[WARNING] Texture '{textureName}' found at '{foundPath}' but not converted - possible mapping issue");
                    }
                    else
                    {
                        ReportProgress($"[WARNING] Texture '{textureName}' exists in pack but marked as missing - possible mapping issue");
                    }
                }
                else
                {
                    actuallyMissing.Add(textureName);
                }
            }

            if (codeIssues.Count > 0)
            {
                ReportProgress($"[WARNING] Found {codeIssues.Count} textures that exist but weren't converted:");
                foreach (var texture in codeIssues.Take(5))
                    ReportProgress($"[WARNING]   - {texture}");
                if (codeIssues.Count > 5)
                    ReportProgress($"[WARNING]   ... and {codeIssues.Count - 5} more");
                ReportProgress("[WARNING] These may indicate mapping issues that need to be fixed");
            }

            if (actuallyMissing.Count > 0)
                ReportProgress($"[INFO] Confirmed {actuallyMissing.Count} textures are genuinely missing from the pack");
        }
        catch (Exception ex)
        {
            ReportProgress($"[ERROR] Failed to validate missing mappings: {ex.Message}");
        }
    }
}
