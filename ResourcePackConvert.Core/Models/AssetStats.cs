namespace ResourcePackConvert.Core.Models;

public class AssetStats : Dictionary<string, int>
{
    public int Total => Values.Sum();
}
