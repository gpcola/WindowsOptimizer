namespace WindowsOptimizer.Models
{
    public sealed class OptimizationDescriptor
    {
        public string Key { get; }
        public string Title { get; }
        public string Description { get; }
        public string Implications { get; }

        public OptimizationDescriptor(string key, string title, string description, string implications)
        {
            Key = key;
            Title = title;
            Description = description;
            Implications = implications;
        }

        public string TooltipText =>
            $"{Description}\n\nImplications:\n{Implications}";
    }
}
