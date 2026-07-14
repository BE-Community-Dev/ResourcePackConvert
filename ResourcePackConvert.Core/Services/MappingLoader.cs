using System.Text.Json;

namespace ResourcePackConvert.Core.Services;

public class MappingLoader
{
    private readonly string _mappingsDir;
    private readonly Dictionary<string, Dictionary<string, object>> _loadedMappings = new();

    public MappingLoader(string mappingsDir = "Mappings")
    {
        _mappingsDir = mappingsDir;
    }

    public Dictionary<string, string> LoadAllMappings()
    {
        var combinedMappings = new Dictionary<string, string>();

        var mappingFile = Path.Combine(_mappingsDir, "textures_mappings.json");
        if (File.Exists(mappingFile))
        {
            Console.WriteLine($@"[INFO] Loading textures_mappings.json...");
            combinedMappings = LoadTextureMappingsFile(mappingFile);
        }
        else if (Directory.Exists(_mappingsDir))
        {
            Console.WriteLine($@"[WARNING] textures_mappings.json not found in {_mappingsDir}");
        }
        else
        {
            combinedMappings = LoadEmbeddedMappings();
        }

        // Populate _loadedMappings for backward compatibility
        if (combinedMappings.Count > 0 && !_loadedMappings.ContainsKey("textures"))
        {
            var data = new Dictionary<string, object>();
            foreach (var kvp in combinedMappings)
                data[kvp.Key] = JsonDocument.Parse($@"""{kvp.Value}""").RootElement.Clone();
            _loadedMappings["textures"] = data;
        }

        Console.WriteLine($@"[INFO] Total mappings loaded: {combinedMappings.Count}");
        return combinedMappings;
    }

    private static Dictionary<string, string> LoadTextureMappingsFile(string filePath)
    {
        var mappings = new Dictionary<string, string>();
        var json = File.ReadAllText(filePath);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            Console.WriteLine($@"[ERROR] textures_mappings.json must be a JSON array, got {doc.RootElement.ValueKind}");
            return mappings;
        }

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            foreach (var prop in element.EnumerateObject())
            {
                var value = prop.Value.GetString();
                if (value != null)
                    mappings[prop.Name] = value;
            }
        }

        return mappings;
    }

    private Dictionary<string, string> LoadEmbeddedMappings()
    {
        var content = EmbeddedResourceHelper.ReadResourceText("textures_mappings.json");
        if (content == null)
        {
            Console.WriteLine($@"[WARNING] Embedded textures_mappings.json not found");
            return new Dictionary<string, string>();
        }

        try
        {
            return ParseTextureMappingsJson(content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"[ERROR] Failed to parse embedded textures_mappings.json: {ex.Message}");
            return new Dictionary<string, string>();
        }
    }

    private static Dictionary<string, string> ParseTextureMappingsJson(string json)
    {
        var mappings = new Dictionary<string, string>();
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            Console.WriteLine($@"[ERROR] textures_mappings.json must be a JSON array, got {doc.RootElement.ValueKind}");
            return mappings;
        }

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            foreach (var prop in element.EnumerateObject())
            {
                var value = prop.Value.GetString();
                if (value != null)
                    mappings[prop.Name] = value;
            }
        }

        return mappings;
    }

    public Dictionary<string, string> GetMappingByCategory(string category)
    {
        if (_loadedMappings.Count == 0)
            LoadAllMappings();

        if (_loadedMappings.TryGetValue(category, out var data))
        {
            var result = new Dictionary<string, string>();
            foreach (var kvp in data)
            {
                if (kvp.Value is JsonElement elem && elem.ValueKind == JsonValueKind.String)
                {
                    var value = elem.GetString();
                    if (value != null)
                        result[kvp.Key] = value;
                }
            }
            return result;
        }

        return new Dictionary<string, string>();
    }

    public List<string> GetCategories()
    {
        if (_loadedMappings.Count == 0)
            LoadAllMappings();

        return _loadedMappings.Keys.ToList();
    }

    public string FindMapping(string javaTexture)
    {
        var allMappings = LoadAllMappings();
        return allMappings.TryGetValue(javaTexture, out var mapped) ? mapped : javaTexture;
    }

    public bool HasMapping(string javaTexture)
    {
        var allMappings = LoadAllMappings();
        return allMappings.ContainsKey(javaTexture);
    }

    public List<string> GetUnmappedTextures(List<string> javaTextures)
    {
        var allMappings = LoadAllMappings();
        return javaTextures.Where(t => !allMappings.ContainsKey(t)).ToList();
    }

    public void SaveMissingMappings(List<string> missingFiles)
    {
        try
        {
            var missingData = new Dictionary<string, object>
            {
                ["description"] = "Missing texture mappings that need manual review",
                ["category"] = "missing",
                ["count"] = missingFiles.Count,
                ["mappings"] = missingFiles.ToDictionary(t => t, t => t)
            };
            var json = JsonSerializer.Serialize(missingData, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText("missing_mappings.json", json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"[WARNING] Failed to save missing mappings: {ex.Message}");
        }
    }

    public Dictionary<string, object> ValidateMappings()
    {
        var allMappings = LoadAllMappings();
        var duplicates = allMappings
            .GroupBy(kvp => kvp.Value)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Key).ToList());

        return new Dictionary<string, object>
        {
            ["total_mappings"] = allMappings.Count,
            ["duplicate_count"] = duplicates.Count,
            ["duplicates"] = duplicates,
            ["categories"] = GetCategories()
        };
    }
}
