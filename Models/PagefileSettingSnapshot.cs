namespace WindowsOptimizer.Models
{
    public sealed class PagefileSettingSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public int InitialSize { get; set; }
        public int MaximumSize { get; set; }
    }
}
