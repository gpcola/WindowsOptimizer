using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WindowsOptimizer
{
    /// <summary>
    /// Central safety boundary for cleanup operations.
    /// Application profiles and user-added exclusions are treated as immutable cleanup targets.
    /// </summary>
    public sealed class CleanupSafety
    {
        private static readonly string[] BuiltInTemplates =
        {
            @"%LOCALAPPDATA%\Microsoft\Edge\User Data",
            @"%LOCALAPPDATA%\Google\Chrome\User Data",
            @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data",
            @"%APPDATA%\Mozilla\Firefox\Profiles",
            @"%LOCALAPPDATA%\Packages",
            @"%LOCALAPPDATA%\Microsoft\OneAuth",
            @"%LOCALAPPDATA%\Microsoft\IdentityCache",
            @"%APPDATA%\Microsoft\Protect",
            @"%APPDATA%\Microsoft\Credentials",
            @"%LOCALAPPDATA%\Microsoft\Credentials"
        };

        private readonly Action<string> log;

        public CleanupSafety(Action<string> logger)
        {
            log = logger;
        }

        public IReadOnlyList<string> BuiltInDisplayPaths => BuiltInTemplates;

        public string ExclusionsFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "1LG Digital",
                "WindowsOptimizer",
                "cleanup-exclusions.txt");

        public IReadOnlyList<string> GetBuiltInProtectedPaths()
        {
            return BuiltInTemplates
                .Select(NormalizePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public IReadOnlyList<string> LoadCustomExclusions()
        {
            try
            {
                if (!File.Exists(ExclusionsFilePath))
                    return Array.Empty<string>();

                return File.ReadAllLines(ExclusionsFilePath)
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
                    .Select(NormalizePath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception ex)
            {
                log("WARNING: Could not read cleanup exclusions: " + ex.Message);
                return Array.Empty<string>();
            }
        }

        public void SaveCustomExclusions(IEnumerable<string> paths)
        {
            string[] normalized = paths
                .Select(path => path?.Trim())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            string? directory = Path.GetDirectoryName(ExclusionsFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllLines(ExclusionsFilePath, normalized);
            log($"Saved {normalized.Length} custom cleanup exclusion(s).");
        }

        public bool IsProtectedPath(string path)
        {
            string? candidate = NormalizePath(path);
            if (string.IsNullOrWhiteSpace(candidate))
                return true;

            foreach (string protectedPath in GetEffectiveProtectedPaths())
            {
                if (PathsOverlap(candidate, protectedPath))
                    return true;
            }

            return false;
        }

        public bool IsSafeTempRoot(string path)
        {
            string? candidate = NormalizePath(path);
            if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
                return false;

            if (IsProtectedPath(candidate) || IsDangerousRoot(candidate))
                return false;

            string leafName = new DirectoryInfo(candidate).Name;
            if (!leafName.Contains("temp", StringComparison.OrdinalIgnoreCase))
                return false;

            string? userTemp = NormalizePath(Path.GetTempPath());
            string? windowsTemp = NormalizePath(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "Temp"));

            return string.Equals(candidate, userTemp, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(candidate, windowsTemp, StringComparison.OrdinalIgnoreCase);
        }

        public bool ShouldSkip(FileSystemInfo item)
        {
            try
            {
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                    return true;
            }
            catch
            {
                return true;
            }

            return IsProtectedPath(item.FullName);
        }

        private IReadOnlyList<string> GetEffectiveProtectedPaths()
        {
            return GetBuiltInProtectedPaths()
                .Concat(LoadCustomExclusions())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool IsDangerousRoot(string path)
        {
            string? candidate = NormalizePath(path);
            if (string.IsNullOrWhiteSpace(candidate))
                return true;

            var dangerous = new[]
            {
                Path.GetPathRoot(candidate),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };

            return dangerous
                .Select(NormalizePath)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Any(value => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase));
        }

        private static bool PathsOverlap(string first, string second)
        {
            return IsSameOrChild(first, second) || IsSameOrChild(second, first);
        }

        private static bool IsSameOrChild(string candidate, string root)
        {
            if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
                return true;

            string prefix = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;

            return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string? NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
                string fullPath = Path.GetFullPath(expanded);
                return Path.TrimEndingDirectorySeparator(fullPath);
            }
            catch
            {
                return null;
            }
        }
    }
}
