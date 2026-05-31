namespace ResourcePackConvert.Core.Models;

public class ConversionStats
{
    public int Converted { get; set; }
    public int Skipped { get; set; }
    public int Missing { get; set; }
    public int Errors { get; set; }

    public int Total => Converted + Skipped + Missing + Errors;

    public ConversionStats Clone()
    {
        return new ConversionStats
        {
            Converted = Converted,
            Skipped = Skipped,
            Missing = Missing,
            Errors = Errors
        };
    }

    public void Add(ConversionStats other)
    {
        Converted += other.Converted;
        Skipped += other.Skipped;
        Missing += other.Missing;
        Errors += other.Errors;
    }
}
