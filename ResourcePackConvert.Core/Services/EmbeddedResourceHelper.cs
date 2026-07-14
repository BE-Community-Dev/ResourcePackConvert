using System.Text;

namespace ResourcePackConvert.Core.Services;

/// <summary>
/// Helper for reading resources embedded in ResourcePackConvert.Core.
/// </summary>
public static class EmbeddedResourceHelper
{
    /// <summary>
    /// Reads all text content from embedded resources matching a given folder prefix.
    /// </summary>
    /// <param name="folderPrefix">e.g. "mappings" or "mappings."</param>
    /// <returns>Dictionary of resource name → text content</returns>
    public static Dictionary<string, string> ReadAllTextFromFolder(string folderPrefix)
    {
        var result = new Dictionary<string, string>();
        var assembly = typeof(EmbeddedResourceHelper).Assembly;

        var prefix = folderPrefix.EndsWith('.') ? folderPrefix : folderPrefix + ".";
        var allResources = assembly.GetManifestResourceNames();

        foreach (var resourceName in allResources)
        {
            // Match resources whose name CONTAINS the prefix (handles namespace variations)
            // e.g. "ResourcePackConvert.Cli.mappings.colored_blocks.json"
            if (!resourceName.Contains("." + prefix, StringComparison.OrdinalIgnoreCase) &&
                !resourceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            // Only .json files
            if (!resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                using var reader = new StreamReader(stream, Encoding.UTF8);
                var content = reader.ReadToEnd();
                result[resourceName] = content;
            }
            catch (Exception ex)
            {
                Console.WriteLine($@"[WARNING] Failed to read embedded resource {resourceName}: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Reads all embedded resource names under a folder prefix.
    /// </summary>
    public static List<string> GetResourceNames(string folderPrefix)
    {
        var assembly = typeof(EmbeddedResourceHelper).Assembly;
        var prefix = folderPrefix.EndsWith('.') ? folderPrefix : folderPrefix + ".";

        return assembly.GetManifestResourceNames()
            .Where(name =>
                name.Contains("." + prefix, StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Reads a single embedded resource as string.
    /// </summary>
    public static string? ReadResourceText(string resourceName)
    {
        var assembly = typeof(EmbeddedResourceHelper).Assembly;

        // Try exact match first
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            // Try partial match (case-insensitive suffix)
            var match = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
            if (match == null) return null;

            using var matchStream = assembly.GetManifestResourceStream(match);
            if (matchStream == null) return null;
            using var reader = new StreamReader(matchStream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        using var reader2 = new StreamReader(stream, Encoding.UTF8);
        return reader2.ReadToEnd();
    }

    /// <summary>
    /// Extracts all embedded resources under a folder prefix to a physical directory.
    /// Useful when a library/process expects file-system access.
    /// </summary>
    public static string ExtractToTemp(string folderPrefix)
    {
        var assembly = typeof(EmbeddedResourceHelper).Assembly;
        var prefix = folderPrefix.EndsWith('.') ? folderPrefix : folderPrefix + ".";

        var tempDir = Path.Combine(Path.GetTempPath(), $"ResourcePackConvert_{folderPrefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.Contains("." + prefix, StringComparison.OrdinalIgnoreCase) &&
                !resourceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                // Derive relative path from resource name
                var relativePath = DeriveRelativePath(resourceName, folderPrefix);
                var outputPath = Path.Combine(tempDir, relativePath);

                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir))
                    Directory.CreateDirectory(outputDir);

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                using var fileStream = File.Create(outputPath);
                stream.CopyTo(fileStream);
            }
            catch (Exception ex)
            {
                Console.WriteLine($@"[WARNING] Failed to extract {resourceName}: {ex.Message}");
            }
        }

        return tempDir;
    }

    private static string DeriveRelativePath(string resourceName, string prefix)
    {
        // resourceName looks like: "ResourcePackConvert.Cli.mappings.colored_blocks.json"
        // We want: "colored_blocks.json"
        var cleanPrefix = prefix.TrimEnd('.');

        // Find the prefix position
        var idx = resourceName.IndexOf(cleanPrefix, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            // Take everything after the prefix + dot
            return resourceName[(idx + cleanPrefix.Length)..].TrimStart('.');
        }

        // Fallback: just take the last segment
        return Path.GetFileName(resourceName);
    }
}
