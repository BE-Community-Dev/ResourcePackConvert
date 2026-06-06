using System.IO.Compression;
using System.Text.Json;
using ResourcePackConvert.Core.Models;

namespace ResourcePackConvert.Core.Services;

public class PackManager
{
    private readonly List<string> _tempDirs = new();

    public string? ExtractJavaPack(string inputPath, string extractDir)
    {
        try
        {
            if (!File.Exists(inputPath))
            {
                Console.WriteLine($@"[ERROR] Input file does not exist: {inputPath}");
                return null;
            }

            if (!inputPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(@"[ERROR] Input file must be a .zip file");
                return null;
            }

            Console.WriteLine($@"[INFO] Extracting Java resource pack: {Path.GetFileName(inputPath)}");

            ZipFile.ExtractToDirectory(inputPath, extractDir);

            var assetsDir = FindAssetsDirectory(extractDir);
            if (assetsDir == null)
            {
                Console.WriteLine(@"[ERROR] Could not find assets/minecraft directory in the Java resource pack");
                return null;
            }

            var minecraftDir = Path.Combine(assetsDir, "minecraft");
            if (!Directory.Exists(minecraftDir))
            {
                Console.WriteLine(@"[ERROR] Could not find minecraft directory in assets");
                return null;
            }

            Console.WriteLine($@"[INFO] Found minecraft directory: {minecraftDir}");
            return minecraftDir;
        }
        catch (InvalidDataException)
        {
            Console.WriteLine($@"[ERROR] Invalid zip file: {inputPath}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"[ERROR] Failed to extract Java resource pack: {ex.Message}");
            return null;
        }
    }

    private string? FindAssetsDirectory(string extractDir)
    {
        foreach (var dir in Directory.GetDirectories(extractDir, "*", SearchOption.AllDirectories))
        {
            var dirName = Path.GetFileName(dir);
            if (dirName.Equals("assets", StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(Path.Combine(dir, "minecraft")))
                    return dir;
            }
        }

        // Fallback: if minecraft folder exists directly
        if (Directory.Exists(Path.Combine(extractDir, "minecraft")))
        {
            var assetsMock = Path.Combine(extractDir, "assets_mock");
            Directory.CreateDirectory(assetsMock);

            var minecraftSrc = Path.Combine(extractDir, "minecraft");
            var minecraftDst = Path.Combine(assetsMock, "minecraft");

            if (!Directory.Exists(minecraftDst))
                Directory.Move(minecraftSrc, minecraftDst);

            return assetsMock;
        }

        return null;
    }

    public bool CreateMcpack(string bedrockTemp, string outputPath)
    {
        try
        {
            if (!outputPath.EndsWith(".mcpack", StringComparison.OrdinalIgnoreCase))
                outputPath = Path.ChangeExtension(outputPath, ".mcpack");

            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
                Directory.CreateDirectory(outputDir);

            Console.WriteLine($@"[INFO] Creating .mcpack file: {outputPath}");

            var fileCount = 0;
            using var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create);

            foreach (var filePath in Directory.GetFiles(bedrockTemp, "*", SearchOption.AllDirectories))
            {
                var entryName = Path.GetRelativePath(bedrockTemp, filePath)
                    .Replace('\\', '/');
                zip.CreateEntryFromFile(filePath, entryName, CompressionLevel.SmallestSize);
                fileCount++;

                if (fileCount % 100 == 0)
                    Console.WriteLine($@"[DEBUG] Added {fileCount} files to .mcpack");
            }

            var fileSize = new FileInfo(outputPath).Length;
            var sizeMb = fileSize / (1024.0 * 1024.0);

            Console.WriteLine($@"[INFO] Created .mcpack file: {outputPath}");
            Console.WriteLine($@"[INFO] Pack size: {sizeMb:F2} MB ({fileCount} files)");

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"[ERROR] Failed to create .mcpack file: {ex.Message}");
            return false;
        }
    }

    public ValidationResult ValidateJavaPack(string inputPath)
    {
        var result = new ValidationResult();

        try
        {
            if (!File.Exists(inputPath))
            {
                result.Errors.Add("File does not exist");
                return result;
            }

            if (!inputPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add("File is not a .zip file");
                return result;
            }

            using var zip = ZipFile.OpenRead(inputPath);
            var fileList = zip.Entries.Select(e => e.FullName).ToList();

            result.HasPackMcmeta = fileList.Any(f => f.Contains("pack.mcmeta"));
            result.HasAssets = fileList.Any(f => f.Contains("assets/"));
            result.HasTextures = fileList.Any(f => f.Contains("textures/") && f.EndsWith(".png"));

            var categories = new HashSet<string>();
            foreach (var texturePath in fileList.Where(f => f.EndsWith(".png") && f.Contains("textures/")))
            {
                var parts = texturePath.Split('/');
                var textureIdx = Array.IndexOf(parts, "textures");
                if (textureIdx >= 0 && textureIdx + 1 < parts.Length)
                    categories.Add(parts[textureIdx + 1]);
            }

            result.TextureCategories = categories.OrderBy(c => c).ToList();

            result.Valid = result.HasAssets && result.HasTextures;

            if (!result.Valid)
            {
                if (!result.HasAssets)
                    result.Errors.Add("No assets directory found");
                if (!result.HasTextures)
                    result.Errors.Add("No texture files found");
            }
        }
        catch (InvalidDataException)
        {
            result.Errors.Add("Invalid or corrupted zip file");
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Validation error: {ex.Message}");
        }

        return result;
    }

    public PackInfo GetPackInfo(string inputPath)
    {
        var packInfo = new PackInfo();

        try
        {
            var fileInfo = new FileInfo(inputPath);
            packInfo.FileSize = fileInfo.Length;

            using var zip = ZipFile.OpenRead(inputPath);
            var fileList = zip.Entries.ToList();

            var textureFiles = fileList.Where(f => f.Name.EndsWith(".png")).ToList();
            packInfo.TextureCount = textureFiles.Count;

            var categories = new HashSet<string>();
            foreach (var entry in textureFiles)
            {
                var parts = entry.FullName.Split('/');
                var textureIdx = Array.IndexOf(parts, "textures");
                if (textureIdx >= 0 && textureIdx + 1 < parts.Length)
                    categories.Add(parts[textureIdx + 1]);
            }
            packInfo.Categories = categories.OrderBy(c => c).ToList();

            var mcmetaEntry = fileList.FirstOrDefault(f => f.FullName.EndsWith("pack.mcmeta"));
            if (mcmetaEntry != null)
            {
                try
                {
                    using var stream = mcmetaEntry.Open();
                    using var doc = JsonDocument.Parse(stream);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("pack", out var packEl))
                    {
                        if (packEl.TryGetProperty("description", out var descEl))
                            packInfo.Description = descEl.GetString() ?? packInfo.Description;
                        if (packEl.TryGetProperty("pack_format", out var formatEl) && formatEl.TryGetInt32(out var fmt))
                            packInfo.PackFormat = fmt;
                    }

                    if (packInfo.Description != "No description")
                        packInfo.Name = packInfo.Description.Length > 50
                            ? packInfo.Description[..50]
                            : packInfo.Description;
                    else
                        packInfo.Name = Path.GetFileNameWithoutExtension(inputPath);
                }
                catch (JsonException)
                {
                    Console.WriteLine(@"[WARNING] Could not parse pack.mcmeta");
                    packInfo.Name = Path.GetFileNameWithoutExtension(inputPath);
                }
            }
            else
            {
                packInfo.Name = Path.GetFileNameWithoutExtension(inputPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"[ERROR] Failed to get pack info: {ex.Message}");
        }

        return packInfo;
    }

    public string CreateTempDirectory(string prefix)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        _tempDirs.Add(tempDir);
        return tempDir;
    }

    public void CleanupTempDirectories()
    {
        foreach (var tempDir in _tempDirs)
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                    Console.WriteLine($@"[DEBUG] Cleaned up temporary directory: {tempDir}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($@"[WARNING] Failed to clean up {tempDir}: {ex.Message}");
                }
            }
        }
        _tempDirs.Clear();
    }

    public AssetStats CopyOtherAssets(string minecraftDir, string bedrockTemp)
    {
        var stats = new AssetStats();

        try
        {
            // --- 1. Copy pack_icon.png from pack.png ---
            // Search locations: extracted root, assets folder, minecraft folder
            var packPngLocations = new[]
            {
                Path.GetFullPath(Path.Combine(minecraftDir, "..", "..", "pack.png")), // extracted root
                Path.GetFullPath(Path.Combine(minecraftDir, "..", "pack.png")),       // assets folder
                Path.Combine(minecraftDir, "pack.png")                                // minecraft folder
            };

            foreach (var packPng in packPngLocations)
            {
                if (File.Exists(packPng))
                {
                    var packIconDest = Path.Combine(bedrockTemp, "pack_icon.png");
                    File.Copy(packPng, packIconDest, true);
                    stats["pack_icon"] = 1;
                    Console.WriteLine($@"[INFO] Copied pack.png as pack_icon.png from {packPng}");
                    break;
                }
            }

            // --- 2. Copy sounds ---
            var javaSounds = Path.Combine(minecraftDir, "sounds");
            var bedrockSounds = Path.Combine(bedrockTemp, "sounds");
            if (Directory.Exists(javaSounds))
            {
                stats["sounds"] = CopyDirectoryRecursive(javaSounds, bedrockSounds);
                Console.WriteLine($@"[INFO] Copied {stats["sounds"]} sound files");
            }

            // --- 3. Copy models ---
            var javaModels = Path.Combine(minecraftDir, "models");
            var bedrockModels = Path.Combine(bedrockTemp, "models");
            if (Directory.Exists(javaModels))
            {
                stats["models"] = CopyDirectoryRecursive(javaModels, bedrockModels);
                Console.WriteLine($@"[INFO] Copied {stats["models"]} model files");
            }

            // --- 4. Copy other dirs/files: fonts/, gpu_warnlist.json, regional_compliancies.json ---
            var otherItems = new[]
            {
                ("fonts", true),   // directory
                ("gpu_warnlist.json", false),
                ("regional_compliancies.json", false)
            };

            foreach (var (itemName, isDir) in otherItems)
            {
                var javaPath = Path.Combine(minecraftDir, itemName);
                var bedrockPath = Path.Combine(bedrockTemp, itemName);

                if (isDir && Directory.Exists(javaPath))
                {
                    stats["other"] += CopyDirectoryRecursive(javaPath, bedrockPath);
                }
                else if (File.Exists(javaPath))
                {
                    var parentDir = Path.GetDirectoryName(bedrockPath);
                    if (!string.IsNullOrEmpty(parentDir))
                        Directory.CreateDirectory(parentDir);
                    File.Copy(javaPath, bedrockPath, true);
                    stats["other"]++;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"[WARNING] Failed to copy some assets: {ex.Message}");
        }

        return stats;
    }

    private int CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        var count = 0;
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
            count++;
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            count += CopyDirectoryRecursive(dir, destSubDir);
        }

        return count;
    }
}
