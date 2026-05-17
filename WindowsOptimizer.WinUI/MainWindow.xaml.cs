using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WindowsOptimizer.Models;

namespace WindowsOptimizer.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly Optimizer optimizer;
    private readonly RestoreManager restoreManager;
    private readonly BenchmarkHelper benchmark;
    private readonly SnapshotManager snapshotManager;
    private readonly Logger logger;
    private readonly StorageAdvisor storageAdvisor;

    private BenchmarkHelper.MetricsSnapshot? beforeSnapshot;
    private BenchmarkHelper.MetricsSnapshot? afterSnapshot;

    private TextBlock? statusText;
    private TextBox? logBox;
    private TextBox? summaryBox;
    private TextBox? advisorBox;
    private TextBox? scanRootBox;
    private TextBox? moveTargetBox;
    private TextBox? wslTargetBox;
    private TextBox? wslVhdSizeBox;
    private ComboBox? pagefileDriveBox;
    private TextBox? pagefileInitialBox;
    private TextBox? pagefileMaximumBox;

    private CheckBox? snapshotCheck;
    private CheckBox? tempCheck;
    private CheckBox? updatesCheck;
    private CheckBox? winSxSCheck;
    private CheckBox? indexingCheck;
    private CheckBox? servicesCheck;
    private CheckBox? backgroundCheck;
    private CheckBox? featuresCheck;
    private CheckBox? bloatCheck;
    private CheckBox? hibernateCheck;
    private CheckBox? restoreCheck;
    private CheckBox? pagefileCheck;

    private ListView? candidateList;
    private ListView? wslList;
    private ListView? networkList;

    private readonly List<StorageCandidate> candidates = new();
    private readonly List<WslDistroEntry> wslDistros = new();

    public MainWindow()
    {
        InitializeComponent();

        logger = new Logger(Log);
        snapshotManager = new SnapshotManager(logger.Log);
        optimizer = new Optimizer(logger.Log);
        restoreManager = new RestoreManager(logger.Log, snapshotManager);
        benchmark = new BenchmarkHelper();
        storageAdvisor = new StorageAdvisor(logger.Log);

        beforeSnapshot = benchmark.CaptureMetrics();
        RootNav.SelectedItem = RootNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();
        Navigate("home");
        Log("Windows Optimizer WinUI by 1LG Digital loaded.");
    }

    private void RootNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            Navigate(tag);
        }
    }

    private void Open1Lg_Click(object sender, RoutedEventArgs e) => OpenExternal("https://www.1lg.com");

    private void Navigate(string tag)
    {
        ContentFrame.Content = tag switch
        {
            "home" => HomePage(),
            "optimize" => OptimizePage(),
            "storage" => StoragePage(),
            "wsl" => WslPage(),
            "network" => NetworkPage(),
            "restore" => RestorePage(),
            "benchmark" => BenchmarkPage(),
            "advisor" => AdvisorPage(),
            "about" => AboutPage(),
            _ => HomePage()
        };
    }

    private FrameworkElement HomePage()
    {
        var page = PagePanel();
        page.Children.Add(Hero("Dashboard", "Storage pressure, optimisation impact, WSL status, network diagnostics, and quick actions in one place."));

        var diskCard = MetricCard("C: free space", $"{DiskHelper.GetFreeSpaceGB("C"):N1} GB", $"of {DiskHelper.GetTotalSpaceGB("C"):N1} GB total", DiskPercentFree());
        var ramCard = MetricCard("Available RAM", $"{beforeSnapshot?.AvailableMemoryMb:N0} MB", "Current snapshot", RamPercentAvailable(beforeSnapshot));
        var wslCard = MetricCard("WSL distros", "Scan", "Open WSL Storage", 0);
        var networkCard = MetricCard("Network adapters", NetworkHelper.GetAdapters().Count.ToString(), "Detected locally", 100);

        page.Children.Add(Wrap(diskCard, ramCard, wslCard, networkCard));
        page.Children.Add(ComparisonDashboard());
        page.Children.Add(ActionCard("Quick actions", "Run the common safe path first, or jump straight to specialist tools.",
            Button("Capture BEFORE", (_, _) => { beforeSnapshot = benchmark.CaptureMetrics(); Navigate("home"); }),
            Button("Capture AFTER", (_, _) => { afterSnapshot = benchmark.CaptureMetrics(); Navigate("home"); }),
            Button("Run common cleanup", async (_, _) => await RunCommonCleanupAsync()),
            Button("WSL Storage", (_, _) => Navigate("wsl")),
            Button("Network", (_, _) => Navigate("network")),
            Button("Storage Advisor", (_, _) => Navigate("advisor"))));
        page.Children.Add(LogCard());
        return Scroll(page);
    }

    private FrameworkElement ComparisonDashboard()
    {
        var panel = Stack();
        panel.Children.Add(Title("Before / after visual comparison"));
        panel.Children.Add(Text("Capture BEFORE, perform an optimisation or cleanup, then capture AFTER to compare impact visually."));

        if (beforeSnapshot == null || afterSnapshot == null)
        {
            panel.Children.Add(Text("Waiting for both snapshots. A baseline BEFORE snapshot is captured automatically at startup."));
            panel.Children.Add(CompareBar("Disk free space", beforeSnapshot?.DiskFreeCgb ?? 0, beforeSnapshot?.DiskFreeCgb ?? 0, "GB"));
            panel.Children.Add(CompareBar("Available RAM", beforeSnapshot?.AvailableMemoryMb ?? 0, beforeSnapshot?.AvailableMemoryMb ?? 0, "MB"));
        }
        else
        {
            panel.Children.Add(CompareBar("Disk free space", beforeSnapshot.DiskFreeCgb, afterSnapshot.DiskFreeCgb, "GB"));
            panel.Children.Add(CompareBar("Available RAM", beforeSnapshot.AvailableMemoryMb, afterSnapshot.AvailableMemoryMb, "MB"));
            panel.Children.Add(CompareBar("Logical processors", beforeSnapshot.LogicalProcessors, afterSnapshot.LogicalProcessors, ""));
        }

        return Card(panel);
    }

    private FrameworkElement OptimizePage()
    {
        var page = PagePanel();
        page.Children.Add(Hero("Optimize", "Run practical cleanup and tuning actions. Recommended actions are visible first; advanced actions stay collapsed."));

        snapshotCheck = Check("Create snapshot backup before running", true);
        tempCheck = Check("Clean temp files", true);
        updatesCheck = Check("Clear update cache", false);
        winSxSCheck = Check("Clean WinSxS component store", false);
        indexingCheck = Check("Disable indexing", false);
        servicesCheck = Check("Disable selected background services", false);
        backgroundCheck = Check("Disable background apps", false);
        featuresCheck = Check("Remove optional features", false);
        bloatCheck = Check("Remove bloat apps", false);
        hibernateCheck = Check("Disable hibernation", false);
        restoreCheck = Check("Delete restore points", false);
        pagefileCheck = Check("Move pagefile", false);

        pagefileDriveBox = new ComboBox { PlaceholderText = "Target drive", MinWidth = 140 };
        foreach (var drive in DiskHelper.GetFixedDriveLetters()) pagefileDriveBox.Items.Add(drive);
        pagefileDriveBox.SelectedItem = pagefileDriveBox.Items.Contains("D") ? "D" : pagefileDriveBox.Items.FirstOrDefault();
        pagefileInitialBox = TextInput("Initial MB", "2048");
        pagefileMaximumBox = TextInput("Maximum MB", "4096");

        page.Children.Add(Card(Stack(Title("Recommended cleanup"), snapshotCheck, tempCheck, updatesCheck, winSxSCheck)));
        page.Children.Add(Expander("Performance and background activity", Stack(indexingCheck, servicesCheck, backgroundCheck)));
        page.Children.Add(Expander("Advanced and higher-risk actions", Stack(featuresCheck, bloatCheck, hibernateCheck, restoreCheck, pagefileCheck, Inline(pagefileDriveBox, pagefileInitialBox, pagefileMaximumBox))));
        page.Children.Add(ActionCard("Run actions", "Pre-run and post-run metrics are captured automatically and reflected on the Dashboard.",
            Button("Run selected", async (_, _) => await RunSelectedAsync()),
            Button("Run common cleanup", async (_, _) => await RunCommonCleanupAsync())));
        summaryBox = MultilineBox("Run summary appears here.", 180);
        page.Children.Add(Card(summaryBox));
        page.Children.Add(LogCard());
        return Scroll(page);
    }

    private FrameworkElement StoragePage()
    {
        var page = PagePanel();
        page.Children.Add(Hero("Storage", "Review user folders, Visual Studio locations, OneDrive status, and large-file candidates."));
        page.Children.Add(ActionCard("Known user folders", "Quick view of Desktop, Documents, Downloads and other shell folders.", Button("Refresh", (_, _) => Navigate("storage"))));
        page.Children.Add(Card(List(storageAdvisor.GetUserFolders().Select(x => $"{x.Name}: {x.CurrentPath} | {x.SizeGb} GB"))));
        page.Children.Add(Expander("Visual Studio storage", Card(List(storageAdvisor.GetVisualStudioLocations().Select(x => $"{x.Name}: {x.Path}\n{x.Notes}")))));

        scanRootBox = TextInput("Scan root", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        moveTargetBox = TextInput("Move target", string.Empty);
        candidateList = List();
        page.Children.Add(ActionCard("Large files and candidates", "Scan user-controlled locations first. Review before moving or deleting.",
            Button("Scan", async (_, _) => await ScanCandidatesAsync()),
            Button("Open selected", (_, _) => OpenSelectedCandidate()),
            Button("Move selected", (_, _) => MoveSelectedCandidate()),
            Button("Delete selected", (_, _) => DeleteSelectedCandidate())));
        page.Children.Add(Card(Stack(scanRootBox, moveTargetBox, candidateList)));
        page.Children.Add(LogCard());
        return Scroll(page);
    }

    private FrameworkElement WslPage()
    {
        var page = PagePanel();
        page.Children.Add(Hero("WSL Storage", "Review distro VHD locations, update file sizes, estimate caches, and move distros safely via export/import."));
        wslTargetBox = TextInput("Move target folder", "D:\\WSL");
        wslVhdSizeBox = TextInput("VHD maximum size", "256GB");
        wslList = List();
        page.Children.Add(ActionCard("WSL actions", "Linux-side cache folders are handled from inside WSL; they are not moved directly from Windows.",
            Button("Scan WSL", async (_, _) => await ScanWslAsync()),
            Button("Refresh sizes", async (_, _) => await RefreshWslSizesAsync()),
            Button("Estimate caches", async (_, _) => await EstimateWslCachesAsync()),
            Button("Move selected safely", async (_, _) => await MoveSelectedWslAsync()),
            Button("Resize selected VHD", async (_, _) => await ResizeSelectedWslAsync()),
            Button("WSL shutdown", async (_, _) => { await WslHelper.ShutdownAsync(); Log("WSL shutdown completed."); })));
        page.Children.Add(Card(Stack(wslTargetBox, wslVhdSizeBox, wslList)));
        page.Children.Add(LogCard());
        return Scroll(page);
    }

    private FrameworkElement NetworkPage()
    {
        var page = PagePanel();
        page.Children.Add(Hero("Network", "Internet, LAN, Ethernet, Wi-Fi, and VPN diagnostics with safe DNS refresh support."));
        networkList = List(NetworkHelper.GetAdapters().Select(a => a.ToString()));
        var output = MultilineBox(NetworkHelper.BuildNetworkSummary(), 260);
        page.Children.Add(ActionCard("Network tools", "Use diagnostics first. Deeper adapter repair should be deliberate and elevated.",
            Button("Refresh adapters", (_, _) => RefreshNetworkList()),
            Button("Show IP config", (_, _) => output.Text = FormatResult(NetworkHelper.ShowIpConfiguration())),
            Button("Show routes", (_, _) => output.Text = FormatResult(NetworkHelper.ShowRoutes())),
            Button("Flush DNS", (_, _) => output.Text = FormatResult(NetworkHelper.FlushDns()))));
        page.Children.Add(Card(networkList));
        page.Children.Add(Card(output));
        return Scroll(page);
    }

    private FrameworkElement RestorePage()
    {
        var page = PagePanel();
        page.Children.Add(Hero("Restore", "Reverse common tuning changes or create a snapshot before further changes."));
        page.Children.Add(ActionCard("Restore actions", "Snapshot restore is best-effort configuration recovery, not a full system image.",
            Button("Re-enable services/background apps", (_, _) => restoreManager.RestoreServicesAndBackgroundApps()),
            Button("Reset pagefile to automatic", (_, _) => restoreManager.RestorePagefileDefault()),
            Button("Re-enable indexing", (_, _) => restoreManager.RestoreIndexing()),
            Button("Create snapshot now", (_, _) => restoreManager.CreateSnapshotNow()),
            Button("Restore latest snapshot", (_, _) => restoreManager.RestoreLatestSnapshot())));
        page.Children.Add(LogCard());
        return Scroll(page);
    }

    private FrameworkElement BenchmarkPage()
    {
        var page = PagePanel();
        page.Children.Add(Hero("Benchmark", "Capture before and after snapshots and review visual comparisons."));
        page.Children.Add(ActionCard("Snapshots", "The same visual comparison is also shown on the Dashboard.",
            Button("Take BEFORE", (_, _) => { beforeSnapshot = benchmark.CaptureMetrics(); Navigate("benchmark"); }),
            Button("Take AFTER", (_, _) => { afterSnapshot = benchmark.CaptureMetrics(); Navigate("benchmark"); }),
            Button("Back to Dashboard", (_, _) => Navigate("home"))));
        page.Children.Add(ComparisonDashboard());
        page.Children.Add(Card(MultilineBox(BuildTextComparison(), 260)));
        return Scroll(page);
    }

    private FrameworkElement AdvisorPage()
    {
        var page = PagePanel();
        page.Children.Add(Hero("Advisor", "Generate readable, local-only recommendations from storage and system scan data."));
        advisorBox = MultilineBox(storageAdvisor.BuildCopilotSummary(), 420);
        page.Children.Add(ActionCard("Advisor actions", "Copy the summary or open relevant Windows surfaces.",
            Button("Refresh", (_, _) => advisorBox.Text = storageAdvisor.BuildCopilotSummary()),
            Button("Open Storage Settings", (_, _) => OpenExternal("ms-settings:storagesense")),
            Button("Open Copilot", (_, _) => OpenExternal("https://copilot.microsoft.com/"))));
        page.Children.Add(Card(advisorBox));
        return Scroll(page);
    }

    private FrameworkElement AboutPage()
    {
        var page = PagePanel();
        page.Children.Add(Hero("1LG Digital", "Windows Optimizer is provided as a practical housekeeping utility by 1LG Digital."));
        page.Children.Add(ActionCard("Contact", "Website: https://www.1lg.com\nEmail: info@1lg.com",
            Button("Visit website", (_, _) => OpenExternal("https://www.1lg.com")),
            Button("Email", (_, _) => OpenExternal("mailto:info@1lg.com?subject=Windows%20Optimizer%20enquiry"))));
        return Scroll(page);
    }

    private async Task RunSelectedAsync()
    {
        var actions = BuildSelectedActions();
        if (!actions.Any()) { Log("No actions selected."); return; }
        await RunActionsAsync(actions);
    }

    private async Task RunCommonCleanupAsync()
    {
        if (tempCheck != null) tempCheck.IsChecked = true;
        if (updatesCheck != null) updatesCheck.IsChecked = true;
        if (winSxSCheck != null) winSxSCheck.IsChecked = true;
        await RunSelectedAsync();
    }

    private async Task RunActionsAsync(List<(string Name, Func<bool> Execute)> actions)
    {
        beforeSnapshot = benchmark.CaptureMetrics();
        if (snapshotCheck?.IsChecked == true) snapshotManager.CreateSnapshot("WinUI pre-run snapshot");
        await Task.Run(() =>
        {
            foreach (var action in actions)
            {
                Log("Running: " + action.Name);
                action.Execute();
            }
        });
        afterSnapshot = benchmark.CaptureMetrics();
        if (summaryBox != null) summaryBox.Text = benchmark.BuildRunSummary(beforeSnapshot, afterSnapshot, actions.Count, true);
        Log("Optimisation run complete. Dashboard comparison updated.");
    }

    private List<(string Name, Func<bool> Execute)> BuildSelectedActions()
    {
        var actions = new List<(string Name, Func<bool> Execute)>();
        if (tempCheck?.IsChecked == true) actions.Add(("Clean temp files", () => optimizer.CleanTempFiles()));
        if (updatesCheck?.IsChecked == true) actions.Add(("Clear update cache", () => optimizer.ClearUpdateCache()));
        if (winSxSCheck?.IsChecked == true) actions.Add(("Clean WinSxS", () => optimizer.CleanWinSxS()));
        if (indexingCheck?.IsChecked == true) actions.Add(("Disable indexing", () => optimizer.DisableIndexing()));
        if (servicesCheck?.IsChecked == true) actions.Add(("Disable services", () => optimizer.DisableServices()));
        if (backgroundCheck?.IsChecked == true) actions.Add(("Disable background apps", () => optimizer.DisableBackgroundApps()));
        if (featuresCheck?.IsChecked == true) actions.Add(("Remove optional features", () => optimizer.RemoveOptionalFeatures()));
        if (bloatCheck?.IsChecked == true) actions.Add(("Remove bloat apps", () => optimizer.RemoveBloatApps()));
        if (hibernateCheck?.IsChecked == true) actions.Add(("Disable hibernation", () => optimizer.DisableHibernation()));
        if (restoreCheck?.IsChecked == true) actions.Add(("Delete restore points", () => optimizer.DeleteRestorePoints()));
        if (pagefileCheck?.IsChecked == true) actions.Add(("Move pagefile", () => optimizer.MovePagefile(new PagefileOptions { DriveLetter = pagefileDriveBox?.SelectedItem?.ToString() ?? "D", InitialSizeMb = ParseInt(pagefileInitialBox?.Text, 2048), MaximumSizeMb = ParseInt(pagefileMaximumBox?.Text, 4096) })));
        return actions;
    }

    private async Task ScanCandidatesAsync()
    {
        candidates.Clear();
        candidateList?.Items.Clear();
        var root = string.IsNullOrWhiteSpace(scanRootBox?.Text) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : scanRootBox!.Text;
        var found = await Task.Run(() => storageAdvisor.ScanCandidates(root));
        candidates.AddRange(found);
        foreach (var item in candidates) candidateList?.Items.Add(Item($"{item.Category} | {item.SizeGb} GB | {item.Safety}\n{item.Path}", item));
        Log($"Candidate scan completed. {found.Count} item(s) listed.");
    }

    private void OpenSelectedCandidate()
    {
        if (candidateList?.SelectedItem is ListViewItem { Tag: StorageCandidate candidate }) storageAdvisor.OpenInExplorer(candidate.Path);
    }

    private void MoveSelectedCandidate()
    {
        if (candidateList?.SelectedItem is not ListViewItem { Tag: StorageCandidate candidate }) return;
        if (string.IsNullOrWhiteSpace(moveTargetBox?.Text) || !Directory.Exists(moveTargetBox.Text)) { Log("Enter an existing move target first."); return; }
        if (storageAdvisor.MoveCandidate(candidate, moveTargetBox.Text)) { candidates.Remove(candidate); candidateList.Items.Remove(candidateList.SelectedItem); }
    }

    private void DeleteSelectedCandidate()
    {
        if (candidateList?.SelectedItem is not ListViewItem { Tag: StorageCandidate candidate }) return;
        if (storageAdvisor.DeleteCandidate(candidate)) { candidates.Remove(candidate); candidateList.Items.Remove(candidateList.SelectedItem); }
    }

    private async Task ScanWslAsync()
    {
        wslDistros.Clear();
        wslList?.Items.Clear();
        foreach (var distro in await WslHelper.GetDistrosAsync()) wslDistros.Add(distro);
        RefreshWslList();
        Log($"Found {wslDistros.Count} WSL distro(s).");
    }

    private void RefreshWslList()
    {
        wslList?.Items.Clear();
        foreach (var d in wslDistros)
        {
            wslList?.Items.Add(Item($"{d.DisplayName} | WSL {d.VersionDisplay} | {d.State}\nVHDX: {d.VhdxPath}\nSize: {d.VhdxSizeDisplay} | Linux used: {d.LinuxUsedDisplay} | Cache: {d.CacheEstimateDisplay}", d));
        }
    }

    private async Task RefreshWslSizesAsync()
    {
        foreach (var distro in wslDistros) await WslHelper.RefreshReportedSizesAsync(distro);
        RefreshWslList();
        Log("WSL file sizes refreshed.");
    }

    private async Task EstimateWslCachesAsync()
    {
        foreach (var distro in wslDistros) await WslHelper.EstimateCacheSizeAsync(distro);
        RefreshWslList();
        Log("WSL cache estimates updated.");
    }

    private async Task MoveSelectedWslAsync()
    {
        if (wslList?.SelectedItem is not ListViewItem { Tag: WslDistroEntry distro }) { Log("Select one WSL distro first."); return; }
        await WslHelper.MoveDistroByExportImportAsync(distro, new WslMoveOptions { TargetRoot = wslTargetBox?.Text ?? "D:\\WSL" }, Log);
    }

    private async Task ResizeSelectedWslAsync()
    {
        if (wslList?.SelectedItem is WslDistroEntry distro) await WslHelper.ResizeDistroVhdAsync(distro, wslVhdSizeBox?.Text ?? "256GB");
        else if (wslList?.SelectedItem is ListViewItem { Tag: WslDistroEntry tagged }) await WslHelper.ResizeDistroVhdAsync(tagged, wslVhdSizeBox?.Text ?? "256GB");
    }

    private void RefreshNetworkList()
    {
        networkList?.Items.Clear();
        foreach (var adapter in NetworkHelper.GetAdapters()) networkList?.Items.Add(adapter.ToString());
    }

    private void Log(string message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}";
            if (statusText != null) statusText.Text = message;
            if (logBox != null) logBox.Text += line + Environment.NewLine;
        });
    }

    private FrameworkElement CompareBar(string label, double before, double after, string unit)
    {
        var max = Math.Max(Math.Max(before, after), 1);
        var delta = after - before;
        var deltaText = delta switch
        {
            > 0 => $"+{delta:N1} {unit}",
            < 0 => $"-{Math.Abs(delta):N1} {unit}",
            _ => $"0 {unit}"
        };

        return Card(Stack(
            new TextBlock { Text = label, FontSize = 16, FontWeight = FontWeights.SemiBold },
            Text($"Before: {before:N1} {unit}"),
            new ProgressBar { Minimum = 0, Maximum = max, Value = before, Height = 8 },
            Text($"After: {after:N1} {unit} ({deltaText})"),
            new ProgressBar { Minimum = 0, Maximum = max, Value = after, Height = 8 }
        ));
    }

    private FrameworkElement MetricCard(string title, string value, string subtext, double percent)
    {
        return Card(Stack(
            new TextBlock { Text = title, FontWeight = FontWeights.SemiBold },
            new TextBlock { Text = value, FontSize = 28, FontWeight = FontWeights.Bold },
            new ProgressBar { Minimum = 0, Maximum = 100, Value = Math.Clamp(percent, 0, 100), Height = 8 },
            Text(subtext)
        ));
    }

    private double DiskPercentFree()
    {
        var total = DiskHelper.GetTotalSpaceGB("C");
        return total <= 0 ? 0 : DiskHelper.GetFreeSpaceGB("C") / total * 100;
    }

    private static double RamPercentAvailable(BenchmarkHelper.MetricsSnapshot? snap)
    {
        if (snap == null || snap.TotalMemoryMbEstimate <= 0) return 0;
        return snap.AvailableMemoryMb / snap.TotalMemoryMbEstimate * 100;
    }

    private string BuildTextComparison()
    {
        if (beforeSnapshot == null || afterSnapshot == null) return "Take both BEFORE and AFTER snapshots to compare.";
        return benchmark.BuildRunSummary(beforeSnapshot, afterSnapshot, 0, false);
    }

    private static StackPanel PagePanel() => new() { Spacing = 12, Padding = new Thickness(20) };
    private static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private static TextBlock Hero(string title, string subtitle) => new() { Text = title + Environment.NewLine + subtitle, TextWrapping = TextWrapping.Wrap, FontSize = 20, Margin = new Thickness(0, 0, 0, 8) };
    private static TextBlock Title(string text) => new() { Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold };
    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private static Border Card(UIElement content) => new() { Child = content, Padding = new Thickness(16), CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 10) };
    private static StackPanel Stack(params UIElement[] children) { var p = new StackPanel { Spacing = 8 }; foreach (var c in children) p.Children.Add(c); return p; }
    private static StackPanel Inline(params UIElement[] children) { var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; foreach (var c in children) p.Children.Add(c); return p; }
    private static WrapPanel Wrap(params UIElement[] children) { var p = new WrapPanel { Orientation = Orientation.Horizontal }; foreach (var c in children) { if (c is FrameworkElement fe) fe.Width = 260; p.Children.Add(c); } return p; }
    private static CheckBox Check(string text, bool isChecked) => new() { Content = text, IsChecked = isChecked };
    private static TextBox TextInput(string placeholder, string text) => new() { PlaceholderText = placeholder, Text = text, MinWidth = 180 };
    private static TextBox MultilineBox(string text, double height) => new() { Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = height, IsReadOnly = false };
    private static Button Button(string text, RoutedEventHandler handler) { var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8) }; b.Click += handler; return b; }
    private static Expander Expander(string header, UIElement content) => new() { Header = header, Content = content, IsExpanded = false };
    private static ListView List() => new() { MinHeight = 260 };
    private static ListView List(IEnumerable<string> items) { var l = List(); foreach (var i in items) l.Items.Add(i); return l; }
    private static ListViewItem Item(string text, object tag) => new() { Content = text, Tag = tag };
    private UIElement LogCard() { statusText = new TextBlock { Text = "Ready" }; logBox = MultilineBox(string.Empty, 180); return Expander("Activity log", Card(Stack(statusText, logBox))); }
    private UIElement ActionCard(string title, string body, params Button[] buttons) => Card(Stack(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold }, Text(body), Inline(buttons)));
    private static int ParseInt(string? value, int fallback) => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    private static string FormatResult(PowerShellResult result) => result.Success ? result.StdOut : result.StdErr;
    private static void OpenExternal(string target) { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { } }
}
