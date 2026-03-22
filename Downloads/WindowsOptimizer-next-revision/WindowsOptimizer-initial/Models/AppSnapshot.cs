using System;
using System.Collections.Generic;

namespace WindowsOptimizer.Models
{
    public sealed class AppSnapshot
    {
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string? Notes { get; set; }
        public Dictionary<string, string?> ServiceStartupTypes { get; set; } = new();
        public string? IndexingStartupType { get; set; }
        public bool BackgroundAppsUserValueExists { get; set; }
        public int? BackgroundAppsUserValue { get; set; }
        public bool BackgroundAppsPolicyValueExists { get; set; }
        public int? BackgroundAppsPolicyValue { get; set; }
        public bool AutomaticManagedPagefile { get; set; }
        public List<PagefileSettingSnapshot> Pagefiles { get; set; } = new();
    }
}
