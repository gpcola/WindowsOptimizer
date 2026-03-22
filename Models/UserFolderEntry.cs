namespace WindowsOptimizer.Models
{
    public class UserFolderEntry
    {
        public string Name { get; set; } = string.Empty;
        public string CurrentPath { get; set; } = string.Empty;
        public double SizeGb { get; set; }
        public bool Exists { get; set; }
    }
}
