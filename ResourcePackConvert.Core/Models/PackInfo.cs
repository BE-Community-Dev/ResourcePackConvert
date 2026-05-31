namespace ResourcePackConvert.Core.Models;

public class PackInfo
{
    public string Name { get; set; } = "Unknown Pack";
    public string Description { get; set; } = "No description";
    public int? PackFormat { get; set; }
    public long FileSize { get; set; }
    public int TextureCount { get; set; }
    public List<string> Categories { get; set; } = new();
}
