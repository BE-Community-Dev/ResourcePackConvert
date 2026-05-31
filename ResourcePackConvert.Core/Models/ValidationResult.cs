namespace ResourcePackConvert.Core.Models;

public class ValidationResult
{
    public bool Valid { get; set; }
    public bool HasPackMcmeta { get; set; }
    public bool HasAssets { get; set; }
    public bool HasTextures { get; set; }
    public List<string> TextureCategories { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
