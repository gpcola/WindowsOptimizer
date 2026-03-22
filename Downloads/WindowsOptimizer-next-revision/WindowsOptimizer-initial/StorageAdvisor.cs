using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using WindowsOptimizer.Models;

namespace WindowsOptimizer
{
    public class StorageAdvisor
    {
        private readonly Action<string> log;

        public StorageAdvisor(Action<string> logger)
        {
            log = logger;
        }

        public List<UserFolderEntry> GetUserFolders()
        {
            var items = new List<UserFolderEntry>();
            AddKnown(items, "Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            AddKnown(items, "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            AddKnown(items, "Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            AddKnown(items, "Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            AddKnown(items, "Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            AddKnown(items, "Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
            return items.OrderByDescending(x => x.SizeGb).ToList();
        }

        public List<PathEntry> GetVisualStudioLocations()
        {
            var paths = new List<PathEntry>();
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            AddPath(paths, "Visual Studio 2022", Path.Combine(pf, "Microsoft Visual Studio", "2022"), "Primary IDE install root. Relocation is typically reinstall-managed.");
            AddPath(paths, "Visual Studio 2019", Path.Combine(pfx86, "Microsoft Visual Studio", "2019"), "Legacy IDE install root if present.");
            AddPath(paths, "VS Package Cache", Path.Combine(programData, "Microsoft", "VisualStudio", "Packages"), "Package cache can consume significant disk and is safer to manage than the IDE root.");
            AddPath(paths, "NuGet Cache", Path.Combine(localAppData, "NuGet", "Cache"), "NuGet caches can be cleaned or redirected separately.");
            AddPath(paths, "vswhere", Path.Combine(pf + " (x86)", "Microsoft Visual Studio", "Installer", "vswhere.exe"), "Instance discovery helper if installed.");
            AddPath(paths, "Visual Studio Installer", Path.Combine(pf + " (x86)", "Microsoft Visual Studio", "Installer", "setup.exe"), "Open installer to modify or relocate via supported reinstall flow.");
            return paths;
        }

        public PathEntry GetOneDriveEntry()
        {
            string? envPath = Environment.GetEnvironmentVariable("OneDrive");
            string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive");
            string root = !string.IsNullOrWhiteSpace(envPath) ? envPath : fallback;
            return new PathEntry
            {
                Name = "OneDrive Root",
                Path = root,
                Exists = Directory.Exists(root),
                Notes = Directory.Exists(root)
                    ? "Use OneDrive's own unlink/relink workflow to relocate the sync root safely."
                    : "OneDrive root not detected in the standard location."
            };
        }

        public List<StorageCandidate> ScanCandidates(string? rootPath = null, int maxResults = 60, CancellationToken cancellationToken = default)
        {
            string root = string.IsNullOrWhiteSpace(rootPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : rootPath!;

            var results = new List<StorageCandidate>();
            if (!Directory.Exists(root))
                return results;

            string[] excludedStarts =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            };

            foreach (string dir in SafeEnumerateDirectories(root, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (excludedStarts.Any(x => !string.IsNullOrWhiteSpace(x) && dir.StartsWith(x, StringComparison.OrdinalIgnoreCase)))
                    continue;

                try
                {
                    var di = new DirectoryInfo(dir);
                    if ((di.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                        continue;

                    double sizeGb = GetDirectorySizeGb(dir, 2, cancellationToken);
                    if (sizeGb >= 0.75)
                        results.Add(BuildCandidate(dir, "Folder", sizeGb, di.LastWriteTime));
                }
                catch
                {
                }
            }

            foreach (string file in SafeEnumerateFiles(root, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var fi = new FileInfo(file);
                    if (fi.Length >= 200L * 1024L * 1024L)
                    {
                        double sizeGb = Math.Round(fi.Length / 1024d / 1024d / 1024d, 2);
                        results.Add(BuildCandidate(file, "File", sizeGb, fi.LastWriteTime));
                    }
                }
                catch
                {
                }
            }

            return results
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.SizeGb)
                .ThenBy(x => x.Path)
                .Take(maxResults)
                .ToList();
        }

        public string BuildCopilotSummary(IEnumerable<StorageCandidate>? preloadedCandidates = null)
        {
            var cFree = DiskHelper.GetFreeSpaceGB("C");
            var cTotal = DiskHelper.GetTotalSpaceGB("C");
            var folders = GetUserFolders().OrderByDescending(x => x.SizeGb).ToList();
            var candidates = (preloadedCandidates?.ToList() ?? ScanCandidates(null, 12)).Take(12).ToList();
            var oneDrive = GetOneDriveEntry();

            var lines = new List<string>
            {
                $"C: drive capacity summary: {cFree} GB free of {cTotal} GB total.",
                "Primary user folders by estimated size:"
            };

            foreach (var folder in folders)
                lines.Add($"- {folder.Name}: {folder.SizeGb} GB at {folder.CurrentPath}");

            lines.Add(string.Empty);
            lines.Add(oneDrive.Exists
                ? $"OneDrive appears to be present at: {oneDrive.Path}. Review whether local sync content is consuming C: drive storage."
                : "OneDrive root was not detected in the standard local path.");

            if (candidates.Any())
            {
                lines.Add(string.Empty);
                lines.Add("Top storage candidates detected:");
                foreach (var item in candidates)
                    lines.Add($"- {item.ItemType}: {item.Path} ({item.SizeGb} GB, risk {item.RiskLevel}, suggested: {item.SuggestedAction})");
            }

            lines.Add(string.Empty);
            lines.Add("Suggested next steps:");
            lines.Add("1. Prioritise large Downloads items, installers, archives, and stale media before changing system settings.");
            lines.Add("2. Use quarantine first for uncertain items instead of permanent deletion.");
            lines.Add("3. Relocate user folders like Documents, Pictures, Videos, or Downloads where appropriate.");
            lines.Add("4. Review OneDrive local storage usage and use the OneDrive workflow for sync-root relocation if needed.");
            lines.Add("5. Use Visual Studio Installer and cache management rather than moving VS files manually.");
            lines.Add("6. Reboot after system-level optimizations before reassessing actual savings.");

            return string.Join(Environment.NewLine, lines);
        }

        public bool MoveCandidate(StorageCandidate candidate, string targetRoot)
        {
            try
            {
                if (candidate == null || string.IsNullOrWhiteSpace(targetRoot) || !Directory.Exists(targetRoot))
                    return false;

                string name = candidate.ItemType == "Folder"
                    ? new DirectoryInfo(candidate.Path).Name
                    : new FileInfo(candidate.Path).Name;

                string destination = Path.Combine(targetRoot, name);
                if (candidate.ItemType == "Folder")
                {
                    if (Directory.Exists(destination))
                        destination = Path.Combine(targetRoot, name + "-moved-" + DateTime.Now.ToString("yyyyMMddHHmmss"));
                    Directory.Move(candidate.Path, destination);
                }
                else
                {
                    if (File.Exists(destination))
                        destination = Path.Combine(targetRoot, Path.GetFileNameWithoutExtension(name) + "-moved-" + DateTime.Now.ToString("yyyyMMddHHmmss") + Path.GetExtension(name));
                    File.Move(candidate.Path, destination);
                }

                log($"Moved candidate to: {destination}");
                return true;
            }
            catch (Exception ex)
            {
                log("ERR: " + ex.Message);
                return false;
            }
        }

        public bool QuarantineCandidate(StorageCandidate candidate)
        {
            try
            {
                if (candidate == null)
                    return false;

                string quarantineRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WindowsOptimizer",
                    "Quarantine",
                    DateTime.Now.ToString("yyyyMMdd"));

                Directory.CreateDirectory(quarantineRoot);
                return MoveCandidate(candidate, quarantineRoot);
            }
            catch (Exception ex)
            {
                log("ERR: " + ex.Message);
                return false;
            }
        }

        public bool DeleteCandidate(StorageCandidate candidate)
        {
            try
            {
                if (candidate == null)
                    return false;

                if (candidate.ItemType == "Folder")
                    Directory.Delete(candidate.Path, true);
                else
                    File.Delete(candidate.Path);

                log($"Deleted candidate permanently: {candidate.Path}");
                return true;
            }
            catch (Exception ex)
            {
                log("ERR: " + ex.Message);
                return false;
            }
        }

        public bool OpenInExplorer(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return false;

                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                    return true;
                }

                if (Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                    return true;
                }
            }
            catch (Exception ex)
            {
                log("ERR: " + ex.Message);
            }

            return false;
        }

        private StorageCandidate BuildCandidate(string path, string itemType, double sizeGb, DateTime lastModified)
        {
            bool isFile = string.Equals(itemType, "File", StringComparison.OrdinalIgnoreCase);
            string category = ClassifyPath(path);
            string risk = GuessRisk(path, isFile, category);
            int score = ScoreCandidate(path, sizeGb, lastModified, risk, category);
            string suggestedAction = risk.Equals("High", StringComparison.OrdinalIgnoreCase) ? "Review" : "Quarantine first";
            if (category == "Installer" || category == "Archive / image")
                suggestedAction = "Quarantine first";
            if (category == "Downloads")
                suggestedAction = "Move or quarantine";

            return new StorageCandidate
            {
                Path = path,
                ItemType = itemType,
                Category = category,
                SizeGb = Math.Round(sizeGb, 2),
                LastModified = lastModified,
                Safety = risk.Equals("Low", StringComparison.OrdinalIgnoreCase) ? "Safer candidate" : "Review first",
                RiskLevel = risk,
                Score = score,
                Recommendation = BuildRecommendation(category, risk, sizeGb, lastModified),
                SuggestedAction = suggestedAction
            };
        }

        private static string BuildRecommendation(string category, string risk, double sizeGb, DateTime lastModified)
        {
            string age = lastModified < DateTime.Now.AddMonths(-6) ? "stale" : "recent";
            if (risk == "Low")
                return $"Large {age.ToLowerInvariant()} {category.ToLowerInvariant()} item. Good candidate for move or quarantine.";
            if (risk == "Medium")
                return $"Large {category.ToLowerInvariant()} item. Inspect before changing it.";
            return $"Potentially sensitive path. Leave in place unless you are sure it is safe to move.";
        }

        private void AddKnown(List<UserFolderEntry> list, string name, string path)
        {
            bool exists = Directory.Exists(path);
            list.Add(new UserFolderEntry
            {
                Name = name,
                CurrentPath = path,
                Exists = exists,
                SizeGb = exists ? Math.Round(GetDirectorySizeGb(path, 3, CancellationToken.None), 2) : 0
            });
        }

        private void AddPath(List<PathEntry> list, string name, string path, string notes)
        {
            list.Add(new PathEntry
            {
                Name = name,
                Path = path,
                Notes = notes,
                Exists = File.Exists(path) || Directory.Exists(path)
            });
        }

        private static IEnumerable<string> SafeEnumerateDirectories(string root, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(root))
                yield break;

            string[] topDirectories;
            try
            {
                topDirectories = Directory.GetDirectories(root);
            }
            catch
            {
                yield break;
            }

            foreach (var top in topDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return top;

                string[] children;
                try
                {
                    children = Directory.GetDirectories(top);
                }
                catch
                {
                    continue;
                }

                foreach (var child in children)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return child;
                }
            }
        }

        private static IEnumerable<string> SafeEnumerateFiles(string root, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(root))
                yield break;

            string[] topDirectories;
            try
            {
                topDirectories = Directory.GetDirectories(root);
            }
            catch
            {
                yield break;
            }

            foreach (var top in topDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string[] files;
                try
                {
                    files = Directory.GetFiles(top);
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return file;
                }
            }
        }

        private static double GetDirectorySizeGb(string path, int depth, CancellationToken cancellationToken)
        {
            long size = 0;
            if (depth < 0 || !Directory.Exists(path))
                return 0;

            try
            {
                foreach (var file in Directory.EnumerateFiles(path))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try { size += new FileInfo(file).Length; } catch { }
                }
                if (depth > 0)
                {
                    foreach (var dir in Directory.EnumerateDirectories(path))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            var di = new DirectoryInfo(dir);
                            if ((di.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                                continue;
                            size += (long)(GetDirectorySizeGb(dir, depth - 1, cancellationToken) * 1024d * 1024d * 1024d);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }

            return size / 1024d / 1024d / 1024d;
        }

        private static string ClassifyPath(string path)
        {
            string lower = path.ToLowerInvariant();
            if (lower.Contains("downloads")) return "Downloads";
            if (lower.EndsWith(".iso") || lower.EndsWith(".zip") || lower.EndsWith(".7z") || lower.EndsWith(".rar")) return "Archive / image";
            if (lower.EndsWith(".msi") || lower.EndsWith(".exe")) return "Installer";
            if (lower.EndsWith(".mp4") || lower.EndsWith(".mov") || lower.EndsWith(".mkv")) return "Video";
            if (lower.Contains("onedrive")) return "OneDrive-managed path";
            if (lower.Contains("visual studio") || lower.Contains("nuget")) return "Development tooling";
            return "User content";
        }

        private static string GuessRisk(string path, bool isFile, string category)
        {
            string lower = path.ToLowerInvariant();
            if (lower.Contains("onedrive")) return "Medium";
            if (category == "Development tooling") return "High";
            if (lower.Contains("desktop") || lower.Contains("documents")) return "High";
            if (lower.Contains("downloads") || category == "Archive / image" || category == "Installer") return "Low";
            if (isFile && (lower.EndsWith(".mp4") || lower.EndsWith(".mov") || lower.EndsWith(".mkv"))) return "Low";
            return "Medium";
        }

        private static int ScoreCandidate(string path, double sizeGb, DateTime lastModified, string risk, string category)
        {
            int score = (int)Math.Round(sizeGb * 10);
            if (lastModified < DateTime.Now.AddMonths(-6)) score += 10;
            if (category == "Downloads") score += 15;
            if (category == "Archive / image" || category == "Installer") score += 20;
            if (category == "Video") score += 10;
            score += risk switch
            {
                "Low" => 20,
                "Medium" => 5,
                _ => -20
            };
            if (path.Length > 180) score += 2;
            return score;
        }
    }
}
