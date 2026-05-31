using ResourcePackConvert.Core.Models;

namespace ResourcePackConvert.Core.Services;

public class TextureConverter
{
    private readonly MappingLoader _mappingLoader;
    private readonly Dictionary<string, string> _textureMappings;
    private readonly HashSet<string> _convertedFiles = new();
    private readonly HashSet<string> _missingFiles = new();
    private readonly HashSet<string> _skippedFiles = new();
    private int _identityMappings = 0; // same name in both editions

    public TextureConverter(string mappingsDir = "mappings")
    {
        _mappingLoader = new MappingLoader(mappingsDir);
        _textureMappings = _mappingLoader.LoadAllMappings();
    }

    /// <summary>
    /// Maps Java texture subdirectory names to Bedrock output subdirectory names.
    /// Supports both pre-1.11 plural (blocks/items) and post-1.11 singular (block/item) conventions.
    /// </summary>
    private static readonly Dictionary<string, string> JavaToBedrockCategoryMap = new()
    {
        // Singular (1.11+) and plural (pre-1.11) both map to Bedrock plural
        ["block"] = "blocks",
        ["blocks"] = "blocks",
        ["item"] = "items",
        ["items"] = "items",
        // Same name in both editions
        ["entity"] = "entity",
        ["environment"] = "environment",
        ["particle"] = "particle",
        ["colormap"] = "colormap",
        ["painting"] = "painting",
        ["font"] = "font",
        ["gui"] = "gui",
        ["map"] = "map",
        ["misc"] = "misc",
        ["models"] = "models",
        ["effect"] = "effect",
    };

    public ConversionStats ConvertTextures(string javaTexturesDir, string bedrockTexturesDir)
    {
        var stats = new ConversionStats();

        if (!Directory.Exists(javaTexturesDir))
        {
            Console.WriteLine($"[WARNING] Java textures directory not found: {javaTexturesDir}");
            return stats;
        }

        // Dynamically discover all texture subdirectories and convert each one
        foreach (var javaSubDir in Directory.GetDirectories(javaTexturesDir))
        {
            var javaDirName = Path.GetFileName(javaSubDir);
            if (JavaToBedrockCategoryMap.TryGetValue(javaDirName, out var bedrockDirName))
            {
                var bedrockSubDir = Path.Combine(bedrockTexturesDir, bedrockDirName);
                stats.Add(ConvertTextureCategory(javaSubDir, bedrockSubDir, bedrockDirName));
            }
            else
            {
                Console.WriteLine($"[DEBUG] Unknown texture category '{javaDirName}' — copying as-is");
                var bedrockSubDir = Path.Combine(bedrockTexturesDir, javaDirName);
                stats.Add(ConvertTextureCategory(javaSubDir, bedrockSubDir, javaDirName));
            }
        }

        if (_missingFiles.Count > 0)
            _mappingLoader.SaveMissingMappings(_missingFiles.ToList());

        if (_identityMappings > 0)
            Console.WriteLine($"[INFO] {_identityMappings} textures use identity mapping (same filename in both editions)");

        return stats;
    }

    private ConversionStats ConvertTextureCategory(string javaDir, string bedrockDir, string category)
    {
        var stats = new ConversionStats();

        Directory.CreateDirectory(bedrockDir);

        var textureExtensions = new[] { ".png", ".tga", ".jpg", ".jpeg" };
        var files = Directory.GetFiles(javaDir, "*.*", SearchOption.AllDirectories)
            .Where(f => textureExtensions.Contains(Path.GetExtension(f).ToLower()));

        foreach (var filePath in files)
        {
            try
            {
                var result = ConvertSingleTexture(filePath, javaDir, bedrockDir, category);
                switch (result)
                {
                    case "converted": stats.Converted++; break;
                    case "skipped": stats.Skipped++; break;
                    case "missing": stats.Missing++; break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Error converting {filePath}: {ex.Message}");
                stats.Errors++;
            }
        }

        Console.WriteLine($"[INFO] Converted {category}: {stats.Converted} files, {stats.Skipped} skipped, {stats.Missing} missing mappings");
        return stats;
    }

    private string ConvertSingleTexture(string filePath, string javaDir, string bedrockDir, string category)
    {
        var fileName = Path.GetFileName(filePath);

        // Skip PBR-specific files (handled by PbrConverter)
        if (fileName.EndsWith("_n.png") || fileName.EndsWith("_s.png"))
            return "skipped";

        var relativePath = Path.GetRelativePath(javaDir, filePath);
        var originalName = Path.GetFileName(filePath);

        string? mappedName = null;
        bool foundInMappings = _textureMappings.TryGetValue(originalName, out mappedName);

        if (!foundInMappings)
        {
            mappedName = GetFallbackMapping(originalName, category);

            if (mappedName == originalName)
            {
                // Identity mapping: same name in both editions — this is normal and expected.
                // No need to log each one individually to avoid spam.
                _identityMappings++;
            }
            else
            {
                // Fallback mapping changed the name — this is a real mapping gap.
                _missingFiles.Add(originalName);
                Console.WriteLine($"[DEBUG] No mapping found for {originalName}, using fallback: {mappedName}");
            }
        }

        var relativeDir = Path.GetDirectoryName(relativePath) ?? "";
        var newRelativePath = Path.Combine(relativeDir, mappedName!);
        var targetPath = Path.Combine(bedrockDir, newRelativePath);

        if (File.Exists(targetPath))
        {
            _skippedFiles.Add(targetPath);
            return "skipped";
        }

        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir))
            Directory.CreateDirectory(targetDir);

        File.Copy(filePath, targetPath, true);
        _convertedFiles.Add(targetPath);

        // Identity mappings (same name both sides) count as "converted"
        // because the texture was correctly placed in the output pack.
        // Only textures where the fallback changed the name count as "missing"
        // (the file was still copied, but under a guessed name).
        if (foundInMappings || mappedName == originalName)
            return "converted";

        // mappedName != originalName but not found in explicit mappings:
        // fallback changed the name, so we're guessing — still count as converted
        // but it was already tracked in _missingFiles for reporting.
        return "converted";
    }

    private string GetFallbackMapping(string textureName, string category)
    {
        if (textureName.StartsWith("minecraft_"))
            textureName = textureName[10..];

        var fallbackPatterns = new Dictionary<string, string>
        {
            ["oak_"] = "",
            ["spruce_"] = "spruce_",
            ["birch_"] = "birch_",
            ["jungle_"] = "jungle_",
            ["acacia_"] = "acacia_",
            ["dark_oak_"] = "dark_oak_",
            ["white_"] = "white_",
            ["orange_"] = "orange_",
            ["magenta_"] = "magenta_",
            ["light_blue_"] = "light_blue_",
            ["yellow_"] = "yellow_",
            ["lime_"] = "lime_",
            ["pink_"] = "pink_",
            ["gray_"] = "gray_",
            ["light_gray_"] = "silver_",
            ["cyan_"] = "cyan_",
            ["purple_"] = "purple_",
            ["blue_"] = "blue_",
            ["brown_"] = "brown_",
            ["green_"] = "green_",
            ["red_"] = "red_",
            ["black_"] = "black_"
        };

        foreach (var (pattern, replacement) in fallbackPatterns)
        {
            if (textureName.StartsWith(pattern))
                return textureName.Replace(pattern, replacement);
        }

        return textureName;
    }

    public Dictionary<string, object> GetConversionReport()
    {
        return new Dictionary<string, object>
        {
            ["converted_files"] = _convertedFiles.Count,
            ["skipped_files"] = _skippedFiles.Count,
            ["missing_mappings"] = _missingFiles.Count,
            ["identity_mappings"] = _identityMappings,
            ["total_mappings_available"] = _textureMappings.Count,
            ["missing_files_list"] = _missingFiles.OrderBy(f => f).ToList(),
            ["converted_files_sample"] = _convertedFiles.OrderBy(f => f).Take(10).ToList()
        };
    }

    public void ReloadMappings()
    {
        var newMappings = _mappingLoader.LoadAllMappings();
        foreach (var kvp in newMappings)
            _textureMappings[kvp.Key] = kvp.Value;
    }
}
