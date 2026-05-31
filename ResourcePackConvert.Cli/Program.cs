using ResourcePackConvert.Core.Services;

if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
{
    PrintHelp();
    return 0;
}

var command = args[0].ToLower();

try
{
    var converter = new ResourcePackConvertConverter(
        progress: new Progress<string>(msg => Console.WriteLine(msg)));

    switch (command)
    {
        case "convert":
            return HandleConvert(converter, args[1..]);

        case "info":
            return HandleInfo(converter);

        case "validate":
            return HandleValidate(converter);

        default:
            Console.WriteLine($"Unknown command: {args[0]}");
            PrintHelp();
            return 1;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[ERROR] Error: {ex.Message}");
    return 1;
}

static int HandleConvert(ResourcePackConvertConverter converter, string[] args)
{
    string? input = null;
    string? output = null;
    string? packName = null;
    string? packDescription = null;
    bool validateInput = true;
    bool enablePbr = true;

    for (int i = 0; i < args.Length; i++)
    {
        var arg = args[i];

        if (arg == "--pack-name" && i + 1 < args.Length)
        {
            packName = args[++i];
        }
        else if (arg == "--pack-description" && i + 1 < args.Length)
        {
            packDescription = args[++i];
        }
        else if (arg == "--no-validation")
        {
            validateInput = false;
        }
        else if (arg == "--disable-pbr")
        {
            enablePbr = false;
        }
        else if (!arg.StartsWith("--"))
        {
            if (input == null)
                input = arg;
            else if (output == null)
                output = arg;
        }
    }

    if (input == null || output == null)
    {
        Console.WriteLine("Usage: ResourcePackConvert.Converter convert <input.zip> <output.mcpack> [options]");
        Console.WriteLine("Options:");
        Console.WriteLine("  --pack-name NAME           Custom pack name");
        Console.WriteLine("  --pack-description DESC    Custom pack description");
        Console.WriteLine("  --no-validation            Skip input pack validation");
        Console.WriteLine("  --disable-pbr              Disable PBR texture conversion");
        return 1;
    }

    var success = converter.ConvertResourcePack(
        input, output,
        packName, packDescription,
        validateInput, enablePbr);

    return success ? 0 : 1;
}

static int HandleInfo(ResourcePackConvertConverter converter)
{
    var info = converter.GetAvailableMappingsInfo();

    Console.WriteLine();
    Console.WriteLine("=== ResourcePackConvert Converter Mapping Information ===");
    Console.WriteLine($"Total mappings: {info["total_mappings"]}");
    Console.WriteLine($"Categories: {info["category_count"]}");
    Console.WriteLine();
    Console.WriteLine("Category details:");

    var categories = (Dictionary<string, object>)info["categories"];
    foreach (var (category, details) in categories)
    {
        var detail = (Dictionary<string, object>)details;
        Console.WriteLine($"  {category}: {detail["count"]} mappings");

        if (detail.TryGetValue("sample_mappings", out var samples) &&
            samples is Dictionary<string, string> sampleDict &&
            sampleDict.Count > 0)
        {
            Console.WriteLine($"    Sample: {string.Join(", ", sampleDict.Keys.Take(3))}");
        }
    }

    Console.WriteLine();
    return 0;
}

static int HandleValidate(ResourcePackConvertConverter converter)
{
    Console.WriteLine();
    Console.WriteLine("=== Validating Mappings ===");

    var validation = converter.ValidateMappings();
    Console.WriteLine($"Total mappings: {validation["total_mappings"]}");
    Console.WriteLine($"Duplicate mappings: {validation["duplicate_count"]}");

    if (validation.TryGetValue("duplicates", out var dupObj) &&
        dupObj is Dictionary<string, List<string>> duplicates &&
        duplicates.Count > 0)
    {
        Console.WriteLine("Duplicates found:");
        foreach (var (javaTexture, bedrockTextures) in duplicates.Take(10))
        {
            Console.WriteLine($"  {javaTexture} -> [{string.Join(", ", bedrockTextures.Take(3))}]");
        }
    }

    var cats = (List<string>)validation["categories"];
    Console.WriteLine($"Categories: {string.Join(", ", cats)}");
    Console.WriteLine();

    return 0;
}

static void PrintHelp()
{
    Console.WriteLine(@"
ResourcePackConvert Resource Pack Converter - C# Edition
Convert Minecraft Java Edition resource packs to Bedrock Edition format

Usage:
  ResourcePackConvert.Converter <command> [options]

Commands:
  convert     Convert a Java resource pack to Bedrock format
  info        Show mapping information
  validate    Validate mappings

Convert Options:
  ResourcePackConvert.Converter convert <input.zip> <output.mcpack> [options]

  --pack-name NAME           Custom pack name
  --pack-description DESC    Custom pack description
  --no-validation            Skip input pack validation
  --disable-pbr              Disable PBR texture conversion

Examples:
  ResourcePackConvert.Converter convert pack.zip output.mcpack
  ResourcePackConvert.Converter convert pack.zip output.mcpack --pack-name ""My Pack""
  ResourcePackConvert.Converter info
  ResourcePackConvert.Converter validate
");
}
