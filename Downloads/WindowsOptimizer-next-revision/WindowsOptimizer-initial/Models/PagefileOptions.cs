namespace WindowsOptimizer.Models
{
    public sealed class PagefileOptions
    {
        public string DriveLetter { get; set; } = "D";
        public int InitialSizeMb { get; set; } = 2048;
        public int MaximumSizeMb { get; set; } = 4096;
    }
}
