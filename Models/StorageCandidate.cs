using System;

namespace WindowsOptimizer.Models
{
    public class StorageCandidate
    {
        public string Path { get; set; } = string.Empty;
        public string ItemType { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public double SizeGb { get; set; }
        public DateTime LastModified { get; set; }
        public string Safety { get; set; } = string.Empty;
    }
}
