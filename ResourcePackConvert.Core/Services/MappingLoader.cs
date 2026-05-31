using System.Text.Json;

namespace ResourcePackConvert.Core.Services;

public class MappingLoader
{
    private readonly string _mappingsDir;
    private readonly Dictionary<string, Dictionary<string, object>> _loadedMappings = new();

    public MappingLoader(string mappingsDir = "mappings")
    {
        _mappingsDir = mappingsDir;
    }

    public Dictionary<string, string> LoadAllMappings()
    {
        var combinedMappings = new Dictionary<string, string>();

        if (!Directory.Exists(_mappingsDir))
        {
            Console.WriteLine($"[WARNING] Mappings directory not found: {_mappingsDir}");
            return combinedMappings;
        }

        var mappingFiles = Directory.GetFiles(_mappingsDir, "*.json");
        if (mappingFiles.Length == 0)
        {
            Console.WriteLine($"[WARNING] No mapping files found in {_mappingsDir}");
            return combinedMappings;
        }

        Console.WriteLine($"[INFO] Loading {mappingFiles.Length} mapping files...");

        foreach (var mappingFile in mappingFiles)
        {
            try
            {
                var categoryMappings = LoadMappingFile(mappingFile);
                if (categoryMappings == null) continue;

                // Read ALL string→string pairs from the root level (Format B)
                // AND from the "mappings" sub-object (Format A)
                // Some files have BOTH — we read both so nothing is missed.
                foreach (var kvp in categoryMappings)
                {
                    // Skip known metadata keys
                    if (kvp.Key is "category" or "description" or "mappings")
                        continue;

                    if (kvp.Value is JsonElement elem && elem.ValueKind == JsonValueKind.String)
                    {
                        var value = elem.GetString();
                        if (value != null)
                            combinedMappings[kvp.Key] = value;
                    }
                }

                // Also read the "mappings" sub-object (if present)
                if (categoryMappings.TryGetValue("mappings", out var mappingsObj) &&
                    mappingsObj is JsonElement mappingsElement &&
                    mappingsElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in mappingsElement.EnumerateObject())
                    {
                        var value = prop.Value.GetString();
                        if (value != null)
                            combinedMappings[prop.Name] = value;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to load mapping file {mappingFile}: {ex.Message}");
            }
        }

        Console.WriteLine($"[INFO] Total mappings loaded: {combinedMappings.Count}");
        return combinedMappings;
    }

    public Dictionary<string, object>? LoadMappingFile(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            var data = new Dictionary<string, object>();

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                data[prop.Name] = prop.Value.Clone();
            }

            var category = data.TryGetValue("category", out var catObj)
                ? catObj.ToString()
                : Path.GetFileNameWithoutExtension(filePath);

            _loadedMappings[category!] = data;
            return data;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[ERROR] Invalid JSON in mapping file {filePath}: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Error loading mapping file {filePath}: {ex.Message}");
            return null;
        }
    }

    public Dictionary<string, string> GetMappingByCategory(string category)
    {
        if (!_loadedMappings.TryGetValue(category, out _))
        {
            var categoryFile = Path.Combine(_mappingsDir, $"{category}.json");
            if (File.Exists(categoryFile))
                LoadMappingFile(categoryFile);
        }

        if (_loadedMappings.TryGetValue(category, out var data))
        {
            var result = new Dictionary<string, string>();

            // Read root-level string→string pairs (Format B)
            foreach (var kvp in data)
            {
                if (kvp.Key is "category" or "description" or "mappings")
                    continue;

                if (kvp.Value is JsonElement elem && elem.ValueKind == JsonValueKind.String)
                {
                    var value = elem.GetString();
                    if (value != null)
                        result[kvp.Key] = value;
                }
            }

            // Also read the "mappings" sub-object (Format A)
            if (data.TryGetValue("mappings", out var mappingsObj) &&
                mappingsObj is JsonElement mappingsElement &&
                mappingsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in mappingsElement.EnumerateObject())
                {
                    var value = prop.Value.GetString();
                    if (value != null)
                        result[prop.Name] = value;
                }
            }

            return result;
        }

        return new Dictionary<string, string>();
    }

    public List<string> GetCategories()
    {
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
            Console.WriteLine($"[WARNING] Failed to save missing mappings: {ex.Message}");
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
