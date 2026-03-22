namespace WindowsOptimizer.Models
{
    public class PathEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public bool Exists { get; set; }
    }
}
