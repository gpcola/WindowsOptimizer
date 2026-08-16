using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            return items;
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

        public List<StorageCandidate> ScanCandidates(string? rootPath = null, int maxResults = 60)
        {
            string root = string.IsNullOrWhiteSpace(rootPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : rootPath!;

            var results = new List<StorageCandidate>();
            if (!Directory.Exists(root))
                return results;

            if (IsProtectedCandidatePath(root, protectUserProfileRootOnly: true, out string rootReason))
            {
                log($"Protected scan root skipped: {rootReason}");
                return results;
            }

            foreach (string dir in SafeEnumerateDirectories(root))
            {
                if (IsProtectedCandidatePath(dir, protectUserProfileRootOnly: false, out _))
                    continue;

                try
                {
                    var di = new DirectoryInfo(dir);
                    if ((di.Attributes & (FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden)) != 0)
                        continue;

                    double sizeGb = GetDirectorySizeGb(dir, 2);
                    if (sizeGb >= 0.75)
                    {
                        results.Add(new StorageCandidate
                        {
                            Path = dir,
                            ItemType = "Folder",
                            Category = ClassifyPath(dir),
                            SizeGb = Math.Round(sizeGb, 2),
                            LastModified = di.LastWriteTime,
                            Safety = GuessSafety(dir, false)
                        });
                    }
                }
                catch { }
            }

            foreach (string file in SafeEnumerateFiles(root))
            {
                if (IsProtectedCandidatePath(file, protectUserProfileRootOnly: false, out _))
                    continue;

                try
                {
                    var fi = new FileInfo(file);
                    if ((fi.Attributes & (FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden)) != 0)
                        continue;

                    if (fi.Length >= 200L * 1024L * 1024L)
                    {
                        results.Add(new StorageCandidate
                        {
                            Path = file,
                            ItemType = "File",
                            Category = ClassifyPath(file),
                            SizeGb = Math.Round(fi.Length / 1024d / 1024d / 1024d, 2),
                            LastModified = fi.LastWriteTime,
                            Safety = GuessSafety(file, true)
                        });
                    }
                }
                catch { }
            }

            return results
                .OrderByDescending(x => x.SizeGb)
                .ThenByDescending(x => x.LastModified)
                .Take(maxResults)
                .ToList();
        }

        public string BuildCopilotSummary()
        {
            var cFree = DiskHelper.GetFreeSpaceGB("C");
            var cTotal = DiskHelper.GetTotalSpaceGB("C");
            var folders = GetUserFolders().OrderByDescending(x => x.SizeGb).ToList();
            var candidates = ScanCandidates(null, 12);
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
                lines.Add("Large move/recycle candidates detected:");
                foreach (var item in candidates)
                    lines.Add($"- {item.ItemType}: {item.Path} ({item.SizeGb} GB, {item.Safety})");
            }

            lines.Add(string.Empty);
            lines.Add("Suggested next steps:");
            lines.Add("1. Move large user files or archives off C: to another drive.");
            lines.Add("2. Relocate user folders like Documents, Pictures, Videos, or Downloads where appropriate.");
            lines.Add("3. Review OneDrive local storage usage and use the OneDrive workflow for sync-root relocation if needed.");
            lines.Add("4. Use Visual Studio Installer and cache management rather than moving VS files manually.");
            lines.Add("5. AppData, browser profiles, OneDrive roots, system paths, hidden/system data, and reparse points are excluded from move/delete candidates.");

            return string.Join(Environment.NewLine, lines);
        }

        public bool MoveCandidate(StorageCandidate candidate, string targetRoot)
        {
            try
            {
                if (candidate == null || string.IsNullOrWhiteSpace(targetRoot) || !Directory.Exists(targetRoot))
                    return false;

                if (IsProtectedCandidatePath(candidate.Path, protectUserProfileRootOnly: false, out string reason))
                {
                    log($"Blocked move from protected application/system data: {reason}");
                    return false;
                }

                if (IsProtectedCandidatePath(targetRoot, protectUserProfileRootOnly: false, out string targetReason))
                {
                    log($"Blocked move into protected application/system data: {targetReason}");
                    return false;
                }

                if (!IsCandidateIdle(candidate.Path))
                {
                    log("Blocked move because one or more files are open, inaccessible, or the folder contains a reparse point. Close the application using the data and retry.");
                    return false;
                }

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

        public bool DeleteCandidate(StorageCandidate candidate)
        {
            try
            {
                if (candidate == null)
                    return false;

                if (IsProtectedCandidatePath(candidate.Path, protectUserProfileRootOnly: false, out string reason))
                {
                    log($"Blocked delete of protected application/system data: {reason}");
                    return false;
                }

                if (!IsCandidateIdle(candidate.Path))
                {
                    log("Blocked delete because one or more files are open, inaccessible, or the folder contains a reparse point. Close the application using the data and retry.");
                    return false;
                }

                if (candidate.ItemType == "Folder")
                {
                    FileSystem.DeleteDirectory(
                        candidate.Path,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin,
                        UICancelOption.DoNothing);
                }
                else
                {
                    FileSystem.DeleteFile(
                        candidate.Path,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin,
                        UICancelOption.DoNothing);
                }

                log($"Moved candidate to Recycle Bin: {candidate.Path}");
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

                if (File.Exists(path) || Directory.Exists(path))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");
                    return true;
                }
            }
            catch (Exception ex)
            {
                log("ERR: " + ex.Message);
            }

            return false;
        }

        private void AddKnown(List<UserFolderEntry> list, string name, string path)
        {
            bool exists = Directory.Exists(path);
            list.Add(new UserFolderEntry
            {
                Name = name,
                CurrentPath = path,
                Exists = exists,
                SizeGb = exists ? Math.Round(GetDirectorySizeGb(path, 3), 2) : 0
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

        private static IEnumerable<string> SafeEnumerateDirectories(string root)
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
                if (IsProtectedCandidatePath(top, protectUserProfileRootOnly: false, out _))
                    continue;

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
                    if (!IsProtectedCandidatePath(child, protectUserProfileRootOnly: false, out _))
                        yield return child;
                }
            }
        }

        private static IEnumerable<string> SafeEnumerateFiles(string root)
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
                if (IsProtectedCandidatePath(top, protectUserProfileRootOnly: false, out _))
                    continue;

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
                    if (!IsProtectedCandidatePath(file, protectUserProfileRootOnly: false, out _))
                        yield return file;
                }
            }
        }

        private static double GetDirectorySizeGb(string path, int depth)
        {
            long size = 0;
            if (depth < 0 || !Directory.Exists(path))
                return 0;

            try
            {
                foreach (var file in Directory.EnumerateFiles(path))
                {
                    try { size += new FileInfo(file).Length; } catch { }
                }
                if (depth > 0)
                {
                    foreach (var dir in Directory.EnumerateDirectories(path))
                    {
                        try
                        {
                            var di = new DirectoryInfo(dir);
                            if ((di.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                                continue;
                            size += (long)(GetDirectorySizeGb(dir, depth - 1) * 1024d * 1024d * 1024d);
                        }
                        catch { }
                    }
                }
            }
            catch { }

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
            return "User content";
        }

        private static string GuessSafety(string path, bool isFile)
        {
            string lower = path.ToLowerInvariant();
            if (lower.Contains("onedrive")) return "Protected";
            if (lower.Contains("downloads") || lower.EndsWith(".iso") || lower.EndsWith(".zip") || lower.EndsWith(".7z") || lower.EndsWith(".msi") || lower.EndsWith(".exe")) return "Review first";
            if (isFile && (lower.EndsWith(".mp4") || lower.EndsWith(".mov") || lower.EndsWith(".mkv"))) return "Review first";
            return "Review first";
        }

        private static bool IsProtectedCandidatePath(string path, bool protectUserProfileRootOnly, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                reason = "empty path";
                return true;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                reason = "invalid path";
                return true;
            }

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd(Path.DirectorySeparatorChar);
            if (protectUserProfileRootOnly && string.Equals(fullPath, userProfile, StringComparison.OrdinalIgnoreCase))
            {
                reason = "user profile root";
                return true;
            }

            var protectedRoots = new List<(string Path, string Name)>
            {
                (Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Windows"),
                (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Program Files"),
                (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Program Files (x86)"),
                (Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ProgramData"),
                (Path.Combine(userProfile, "AppData"), "AppData"),
                (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Local AppData"),
                (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Roaming AppData")
            };

            string? oneDrive = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrWhiteSpace(oneDrive))
                protectedRoots.Add((oneDrive, "OneDrive sync root"));

            foreach (var entry in protectedRoots)
            {
                if (string.IsNullOrWhiteSpace(entry.Path))
                    continue;

                string protectedPath;
                try
                {
                    protectedPath = Path.GetFullPath(entry.Path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch
                {
                    continue;
                }

                if (IsSameOrChild(fullPath, protectedPath))
                {
                    reason = entry.Name;
                    return true;
                }
            }

            try
            {
                FileAttributes attributes;
                if (File.Exists(fullPath)) attributes = File.GetAttributes(fullPath);
                else if (Directory.Exists(fullPath)) attributes = new DirectoryInfo(fullPath).Attributes;
                else return false;

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    reason = "reparse point";
                    return true;
                }
                if ((attributes & FileAttributes.System) != 0)
                {
                    reason = "system data";
                    return true;
                }
                if ((attributes & FileAttributes.Hidden) != 0)
                {
                    reason = "hidden data";
                    return true;
                }
            }
            catch
            {
                reason = "inaccessible metadata";
                return true;
            }

            return false;
        }

        private static bool IsSameOrChild(string path, string root)
        {
            if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
                return true;

            string prefix = root + Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCandidateIdle(string path)
        {
            try
            {
                if (File.Exists(path))
                    return CanOpenExclusively(path);

                if (!Directory.Exists(path))
                    return false;

                var pending = new Stack<string>();
                pending.Push(path);

                while (pending.Count > 0)
                {
                    string current = pending.Pop();
                    var currentInfo = new DirectoryInfo(current);
                    if ((currentInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                        return false;

                    foreach (string file in Directory.EnumerateFiles(current))
                    {
                        if (!CanOpenExclusively(file))
                            return false;
                    }

                    foreach (string dir in Directory.EnumerateDirectories(current))
                    {
                        var di = new DirectoryInfo(dir);
                        if ((di.Attributes & FileAttributes.ReparsePoint) != 0)
                            return false;
                        pending.Push(dir);
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool CanOpenExclusively(string filePath)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
