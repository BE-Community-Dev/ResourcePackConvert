using System.Text.Json;

namespace ResourcePackConvert.Core.Services;

public class BedrockStructureGenerator
{
    private static string ResolveRequiredPath(string relativePath)
    {
        var assemblyDir = AppContext.BaseDirectory;
        var assemblyRelative = Path.Combine(assemblyDir, relativePath);
        if (File.Exists(assemblyRelative))
            return assemblyRelative;

        return relativePath; // fall back to working-directory-relative
    }

    private readonly string[] _requiredDirectories =
    [
        "textures/blocks",
        "textures/items",
        "textures/entity",
        "textures/environment",
        "textures/particle",
        "textures/ui",
        "textures/colormap",
        "textures/painting",
        "models",
        "sounds",
        "texts",
        "animations",
        "entity",
        "fogs",
        "render_controllers",
        "materials"
    ];

    public void CreateBedrockStructure(string bedrockTemp)
    {
        Console.WriteLine(@"[INFO] Creating Bedrock Edition directory structure...");

        foreach (var dirPath in _requiredDirectories)
        {
            var fullPath = Path.Combine(bedrockTemp, dirPath);
            Directory.CreateDirectory(fullPath);
        }
    }

    public bool GenerateManifest(string bedrockTemp, string? packName = null,
        string? packDescription = null, int[]? version = null, bool enablePbr = false)
    {
        try
        {
            packName ??= "Converted Java Resource Pack";
            packDescription ??= $"Converted from Java Edition on {DateTime.Now:yyyy-MM-dd HH:mm}";
            version ??= [1, 0, 0];

            var manifestObj = new Dictionary<string, object>
            {
                ["format_version"] = 2,
                ["header"] = new Dictionary<string, object>
                {
                    ["description"] = packDescription,
                    ["name"] = packName,
                    ["uuid"] = Guid.NewGuid().ToString(),
                    ["version"] = version,
                    ["min_engine_version"] = new[] { 1, 21, 0 }
                },
                ["modules"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["description"] = packDescription,
                        ["type"] = "resources",
                        ["uuid"] = Guid.NewGuid().ToString(),
                        ["version"] = version
                    }
                }
            };

            if (enablePbr)
            {
                manifestObj["capabilities"] = new[] { "pbr", "raytraced" };
                Console.WriteLine(@"[INFO] Enabled PBR and raytraced capabilities in manifest");
            }

            var manifestPath = Path.Combine(bedrockTemp, "manifest.json");
            var json = JsonSerializer.Serialize(manifestObj, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(manifestPath, json);

            Console.WriteLine($@"[INFO] Generated manifest.json: {packName} v{string.Join(".", version)}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"[ERROR] Failed to generate manifest.json: {ex.Message}");
            return false;
        }
    }

    public bool GenerateTerrainTextureJson(string bedrockTemp, string packName = "converted_pack")
    {
        return CopyRequiredFile("required/terrain_texture.json",
            Path.Combine(bedrockTemp, "textures", "terrain_texture.json"));
    }

    public bool GenerateItemTextureJson(string bedrockTemp, string packName = "converted_pack")
    {
        return CopyRequiredFile("required/item_texture.json",
            Path.Combine(bedrockTemp, "textures", "item_texture.json"));
    }

    public bool GenerateBlocksJson(string bedrockTemp)
    {
        return CopyRequiredFile("required/blocks.json",
            Path.Combine(bedrockTemp, "blocks.json"));
    }

    private bool CopyRequiredFile(string sourcePath, string destPath)
    {
        try
        {
            var resolvedSource = ResolveRequiredPath(sourcePath);
            if (!File.Exists(resolvedSource))
            {
                // Try embedded resource fallback
                var embeddedContent = EmbeddedResourceHelper.ReadResourceText(sourcePath);
                if (embeddedContent != null)
                {
                    var destFolder = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destFolder))
                        Directory.CreateDirectory(destFolder);

                    File.WriteAllText(destPath, embeddedContent);
                    Console.WriteLine($@"[INFO] Extracted {Path.GetFileName(sourcePath)} from embedded resources");
                    return true;
                }

                Console.WriteLine($@"[ERROR] Required file not found: {sourcePath} (resolved: {resolvedSource})");
                return false;
            }

            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            File.Copy(resolvedSource, destPath, true);
            Console.WriteLine($@"[INFO] Copied {Path.GetFileName(sourcePath)} from required folder");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"[ERROR] Failed to copy {sourcePath}: {ex.Message}");
            return false;
        }
    }

    public bool GeneratePackIcon(string bedrockTemp, string? iconPath = null)
    {
        try
        {
            var packIconPath = Path.Combine(bedrockTemp, "pack_icon.png");

            if (iconPath != null && File.Exists(iconPath))
            {
                File.Copy(iconPath, packIconPath, true);
                Console.WriteLine($@"[INFO] Copied custom pack icon from {iconPath}");
            }
            else
            {
                Console.WriteLine(@"[INFO] No custom icon provided, pack will use default Bedrock icon");
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"[ERROR] Failed to handle pack icon: {ex.Message}");
            return false;
        }
    }

    public bool GenerateLanguageFiles(string bedrockTemp, string? javaLangDir = null)
    {
        try
        {
            var textsDir = Path.Combine(bedrockTemp, "texts");
            Directory.CreateDirectory(textsDir);

            if (javaLangDir != null && Directory.Exists(javaLangDir))
            {
                foreach (var langFile in Directory.GetFiles(javaLangDir, "*.json"))
                {
                    ConvertLangFile(langFile, textsDir);
                }
            }
            else
            {
                var enUsPath = Path.Combine(textsDir, "en_US.lang");
                File.WriteAllText(enUsPath,
                    "## Converted Java Resource Pack\n" +
                    "## Language file converted from Java Edition\n");

                Console.WriteLine(@"[INFO] Created basic en_US.lang file");
            }

            // Generate languages.json
            var languagesData = new Dictionary<string, object>
            {
                ["language"] = new[]
                {
                    new object[] { "en_US", "English (US)", "English (US)", 100 }
                }
            };

            var languagesPath = Path.Combine(textsDir, "languages.json");
            var json = JsonSerializer.Serialize(languagesData, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(languagesPath, json);

            Console.WriteLine(@"[INFO] Generated language files");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"[ERROR] Failed to generate language files: {ex.Message}");
            return false;
        }
    }

    private void ConvertLangFile(string javaLangFile, string textsDir)
    {
        try
        {
            var json = File.ReadAllText(javaLangFile);
            using var doc = JsonDocument.Parse(json);

            var locale = Path.GetFileNameWithoutExtension(javaLangFile);
            var bedrockLocale = ConvertLocaleCode(locale);

            var bedrockLangFile = Path.Combine(textsDir, $"{bedrockLocale}.lang");
            using var writer = new StreamWriter(bedrockLangFile);

            writer.WriteLine($"## Converted from Java Edition {locale}.json");
            writer.WriteLine($"## Generated on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine();

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var key = ConvertLangKey(prop.Name);
                var value = prop.Value.GetString() ?? "";
                writer.WriteLine($"{key}={value}");
            }

            Console.WriteLine($@"[DEBUG] Converted language file: {Path.GetFileName(javaLangFile)} -> {Path.GetFileName(bedrockLangFile)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"[WARNING] Failed to convert language file {javaLangFile}: {ex.Message}");
        }
    }

    private string ConvertLocaleCode(string javaLocale)
    {
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en_us"] = "en_US",
            ["en_gb"] = "en_GB",
            ["de_de"] = "de_DE",
            ["es_es"] = "es_ES",
            ["es_mx"] = "es_MX",
            ["fr_fr"] = "fr_FR",
            ["fr_ca"] = "fr_CA",
            ["it_it"] = "it_IT",
            ["ja_jp"] = "ja_JP",
            ["ko_kr"] = "ko_KR",
            ["pt_br"] = "pt_BR",
            ["pt_pt"] = "pt_PT",
            ["ru_ru"] = "ru_RU",
            ["zh_cn"] = "zh_CN",
            ["zh_tw"] = "zh_TW"
        };

        return mappings.TryGetValue(javaLocale, out var mapped) ? mapped : "en_US";
    }

    private string ConvertLangKey(string javaKey)
    {
        var keyMappings = new Dictionary<string, string>
        {
            ["block.minecraft."] = "tile.",
            ["item.minecraft."] = "item.",
            ["entity.minecraft."] = "entity.",
            ["enchantment.minecraft."] = "enchantment.",
            ["effect.minecraft."] = "effect.",
            ["biome.minecraft."] = "biome."
        };

        foreach (var (javaPrefix, bedrockPrefix) in keyMappings)
        {
            if (javaKey.StartsWith(javaPrefix))
                return javaKey.Replace(javaPrefix, bedrockPrefix);
        }

        return javaKey;
    }

    public Dictionary<string, List<string>> ValidateBedrockStructure(string bedrockTemp)
    {
        var issues = new Dictionary<string, List<string>>
        {
            ["missing_required_files"] = new(),
            ["missing_directories"] = new(),
            ["invalid_json_files"] = new()
        };

        var requiredFiles = new[]
        {
            "manifest.json",
            "textures/terrain_texture.json",
            "textures/item_texture.json"
        };

        foreach (var requiredFile in requiredFiles)
        {
            if (!File.Exists(Path.Combine(bedrockTemp, requiredFile)))
                issues["missing_required_files"].Add(requiredFile);
        }

        foreach (var requiredDir in _requiredDirectories)
        {
            if (!Directory.Exists(Path.Combine(bedrockTemp, requiredDir)))
                issues["missing_directories"].Add(requiredDir);
        }

        foreach (var jsonFile in Directory.GetFiles(bedrockTemp, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                var content = File.ReadAllText(jsonFile);
                JsonDocument.Parse(content);
            }
            catch (JsonException)
            {
                var relativePath = Path.GetRelativePath(bedrockTemp, jsonFile);
                issues["invalid_json_files"].Add(relativePath);
            }
        }

        return issues;
    }
}
