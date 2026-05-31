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
    /// Optional progress callback — reports conversion stage with message and percentage.
    /// Subscribe via <c>new Progress&lt;ConversionProgress&gt;(p => ...)</c>.
    /// </summary>
    private readonly IProgress<ConversionProgress>? _progress;

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

    private void ReportProgress(string message, int percentage, string? stage = null)
    {
        _progress?.Report(new ConversionProgress(message, percentage, stage));
    }

    /// <summary>
    /// Creates a converter with optional progress reporting.
    /// </summary>
    /// <param name="mappingsDir">Folder name for mapping files (embedded or on-disk).</param>
    /// <param name="progress">Optional <see cref="IProgress{T}"/> callback receiving <see cref="ConversionProgress"/>.</param>
    public ResourcePackConvertConverter(string mappingsDir = "Mappings", IProgress<ConversionProgress>? progress = null)
    {
        _progress = progress;
        _mappingsDir = ResolveDataPath(mappingsDir);

        _mappingLoader = new MappingLoader(_mappingsDir);
        _packManager = new PackManager();
        _textureConverter = new TextureConverter(_mappingsDir);
        _bedrockGenerator = new BedrockStructureGenerator();
        _pbrConverter = new PbrConverter();

        _textureMappings = _mappingLoader.LoadAllMappings();
        ReportProgress($"Initialized converter with {_textureMappings.Count} texture mappings", 0, "Init");
    }

    public bool ConvertResourcePack(string inputPath, string outputPath,
        string? packName = null, string? packDescription = null,
        bool validateInput = true, bool enablePbr = true)
    {
        try
        {
            ReportProgress($"Starting conversion of {Path.GetFileName(inputPath)}", 1, "Start");

            if (validateInput)
            {
                ReportProgress("Validating input pack...", 2, "Validate");
                if (!ValidateInputPack(inputPath))
                    return false;
            }

            ReportProgress("Preparing workspace...", 5, "Prepare");
            var tempDir = _packManager.CreateTempDirectory("ResourcePackConvert_conversion");
            var javaTemp = Path.Combine(tempDir, "java_extracted");
            var bedrockTemp = Path.Combine(tempDir, "bedrock_build");

            Directory.CreateDirectory(javaTemp);
            Directory.CreateDirectory(bedrockTemp);

            ReportProgress("Extracting Java pack...", 8, "Extract");
            var minecraftDir = _packManager.ExtractJavaPack(inputPath, javaTemp);
            if (minecraftDir == null)
                return false;

            var packInfo = _packManager.GetPackInfo(inputPath);
            packName ??= packInfo.Name;
            packDescription ??= packInfo.Description;

            ReportProgress($"Converting pack: {packName}", 10, "Structure");
            ReportProgress($"Found {packInfo.TextureCount} textures in {packInfo.Categories.Count} categories", 10);

            ReportProgress("Creating Bedrock structure...", 12, "Structure");
            _bedrockGenerator.CreateBedrockStructure(bedrockTemp);
            if (!_bedrockGenerator.GenerateManifest(bedrockTemp, packName, packDescription, enablePbr: enablePbr))
            {
                ReportProgress("Failed to generate manifest.json", 12);
                return false;
            }

            ReportProgress("Converting textures...", 15, "Textures");
            var javaTexturesDir = Path.Combine(minecraftDir, "textures");
            var bedrockTexturesDir = Path.Combine(bedrockTemp, "textures");

            var conversionStats = _textureConverter.ConvertTextures(javaTexturesDir, bedrockTexturesDir);
            LogConversionStats(conversionStats);

            PbrStats? pbrStats = null;
            if (enablePbr)
            {
                ReportProgress("Processing PBR textures...", 55, "PBR");
                pbrStats = _pbrConverter.ConvertPbrTextures(javaTexturesDir, bedrockTexturesDir, _textureMappings);
                LogPbrStats(pbrStats);
            }
            else
            {
                ReportProgress("Skipping PBR conversion", 60, "PBR");
            }

            ReportProgress("Validating missing mappings...", 65, "Validate");
            ValidateMissingMappings(javaTexturesDir);

            ReportProgress("Copying required files...", 70, "Copy");
            _bedrockGenerator.GenerateTerrainTextureJson(bedrockTemp);
            _bedrockGenerator.GenerateItemTextureJson(bedrockTemp);
            _bedrockGenerator.GenerateBlocksJson(bedrockTemp);

            var javaLangDir = Path.Combine(minecraftDir, "lang");
            _bedrockGenerator.GenerateLanguageFiles(bedrockTemp, javaLangDir);

            var assetStats = _packManager.CopyOtherAssets(minecraftDir, bedrockTemp);
            LogAssetStats(assetStats);

            ReportProgress($"Creating .mcpack: {Path.GetFileName(outputPath)}", 80, "Pack");
            if (!_packManager.CreateMcpack(bedrockTemp, outputPath))
            {
                ReportProgress("Failed to create .mcpack file", 80);
                return false;
            }

            GenerateConversionReport(conversionStats, assetStats, pbrStats, outputPath, enablePbr);

            ReportProgress("Conversion completed successfully!", 100, "Done");
            ReportProgress($"Output saved to: {outputPath}", 100, "Done");
            if (enablePbr)
                ReportProgress("PBR textures converted - pack supports RTX/ray tracing features", 100);

            return true;
        }
        catch (Exception ex)
        {
            ReportProgress($"Conversion failed: {ex.Message}", 100);
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
            ReportProgress("Input pack validation failed:", 4);
            foreach (var error in validationResult.Errors)
                ReportProgress($"  - {error}", 4);
            return false;
        }

        ReportProgress("Input pack validation passed", 4);
        ReportProgress($"Pack has {validationResult.TextureCategories.Count} texture categories: {string.Join(", ", validationResult.TextureCategories)}", 4);
        return true;
    }

    private void LogConversionStats(ConversionStats stats)
    {
        ReportProgress($"Texture conversion: {stats.Converted} converted, {stats.Skipped} skipped, {stats.Missing} missing, {stats.Errors} errors", 50);
        if (stats.Missing > 0)
            ReportProgress($"WARNING: {stats.Missing} textures have missing mappings", 50);
    }

    private void LogPbrStats(PbrStats stats)
    {
        ReportProgress($"PBR: {stats.SpecularConverted} specular, {stats.NormalConverted} normal, {stats.MerGenerated} MER, {stats.TextureSetsCreated} texture sets", 60);
    }

    private void LogAssetStats(AssetStats stats)
    {
        if (stats.Total > 0)
        {
            ReportProgress($"Assets copied: {stats.Total} files", 78);
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

            ReportProgress($"Report saved to: {reportPath}", 95);
        }
        catch (Exception ex)
        {
            ReportProgress($"Failed to generate report: {ex.Message}", 95);
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
                ReportProgress("No missing_mappings.json found — all textures mapped", 66);
                return;
            }

            var json = File.ReadAllText(missingFile);
            using var doc = JsonDocument.Parse(json);
            var missingTextures = new List<string>();

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
                    codeIssues.Add(textureName);
                else
                    actuallyMissing.Add(textureName);
            }

            if (codeIssues.Count > 0)
                ReportProgress($"WARNING: {codeIssues.Count} textures exist but weren't converted — possible mapping issue", 68);

            if (actuallyMissing.Count > 0)
                ReportProgress($"Confirmed {actuallyMissing.Count} textures genuinely missing from the pack", 68);
        }
        catch (Exception ex)
        {
            ReportProgress($"Failed to validate missing mappings: {ex.Message}", 68);
        }
    }
}
