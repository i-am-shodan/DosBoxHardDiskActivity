namespace DosBoxHardDiskActivity;

public class AppConfiguration
{
    public List<string> Directories { get; set; } = new();
    public int GpioPin { get; set; } = 4;
}

public class SoundsConfiguration
{
    public string LongActivity { get; set; } = string.Empty;
    public string ShortActivity { get; set; } = string.Empty;
}
