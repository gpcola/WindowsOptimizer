using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindowsOptimizer
{
    public partial class MainWindow
    {
        private bool quickModeInitialized;
        private Button quickCleanButton = null!;
        private TextBlock quickCleanStatus = null!;
        private TabControl modeTabs = null!;

        public void EnableQuickModeShell()
        {
            if (quickModeInitialized || Content is not UIElement advancedContent)
                return;

            quickModeInitialized = true;
            Content = null;

            modeTabs = new TabControl
            {
                Margin = new Thickness(12),
                SelectedIndex = 0
            };

            var quickTab = new TabItem
            {
                Header = "Quick Clean",
                Content = BuildQuickCleanContent(),
                IsSelected = true
            };

            var advancedTab = new TabItem
            {
                Header = "Advanced",
                Content = advancedContent
            };

            modeTabs.Items.Add(quickTab);
            modeTabs.Items.Add(advancedTab);
            modeTabs.SelectionChanged += ModeTabs_SelectionChanged;

            Content = modeTabs;
            Title = "Windows Optimizer";

            MinWidth = 640;
            MinHeight = 430;
            Width = 760;
            Height = 540;
        }

        private FrameworkElement BuildQuickCleanContent()
        {
            var root = new Grid
            {
                Margin = new Thickness(34),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var card = new Border
            {
                MaxWidth = 620,
                Padding = new Thickness(30),
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                BorderBrush = SystemColors.ActiveBorderBrush,
                Background = SystemColors.ControlBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var panel = new StackPanel();
            card.Child = panel;

            panel.Children.Add(new TextBlock
            {
                Text = "Quick Clean",
                FontSize = 28,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "One click removes stale, unlocked temporary files, empties the Recycle Bin, clears the Windows Update download cache only when Windows Update is idle, and removes a conservative allowlist of optional apps.",
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = 540,
                Margin = new Thickness(0, 0, 0, 12)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Application data and browser profiles (including Microsoft Edge), indexing, optional Windows features, pagefile settings and existing service configuration are left untouched. In-use items are skipped rather than forced.",
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = 540,
                Opacity = 0.78,
                Margin = new Thickness(0, 0, 0, 24)
            });

            quickCleanButton = new Button
            {
                Content = "Clean up this PC",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Height = 62,
                MinWidth = 300,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 18)
            };
            quickCleanButton.Click += QuickClean_Click;
            panel.Children.Add(quickCleanButton);

            quickCleanStatus = new TextBlock
            {
                Text = "Ready. Safety checks will skip anything that is active or uncertain.",
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = 540,
                FontWeight = FontWeights.SemiBold
            };
            panel.Children.Add(quickCleanStatus);

            Grid.SetRow(card, 1);
            root.Children.Add(card);
            return root;
        }

        private void ModeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, modeTabs))
                return;

            if (modeTabs.SelectedIndex == 1)
            {
                MinWidth = 1080;
                MinHeight = 720;
                if (Width < 1220) Width = 1220;
                if (Height < 820) Height = 820;
            }
            else
            {
                MinWidth = 640;
                MinHeight = 430;
            }
        }

        private async void QuickClean_Click(object sender, RoutedEventArgs e)
        {
            if (isBusy)
            {
                quickCleanStatus.Text = "Another operation is already running.";
                return;
            }

            quickCleanButton.IsEnabled = false;
            quickCleanStatus.Text = "Cleaning safely. Active or uncertain items will be skipped...";

            var actions = new List<(string Name, Func<bool> Execute)>
            {
                ("Clean stale temp files", () => optimizer.CleanTempFiles()),
                ("Empty Recycle Bin", EmptyRecycleBin),
                ("Clear Windows Update cache", () => optimizer.ClearUpdateCache()),
                ("Remove optional apps", () => optimizer.RemoveBloatApps())
            };

            try
            {
                await RunWorkflowAsync(actions, "Quick clean", shouldCreateSnapshot: false);
                quickCleanStatus.Text = "Finished. Protected application data, browser profiles and active files were not touched. Open Advanced to review the activity log.";
            }
            catch (Exception ex)
            {
                quickCleanStatus.Text = "Quick clean stopped safely after an error. Open Advanced to review the activity log.";
                logger.Log("ERR: Quick clean: " + ex.Message);
            }
            finally
            {
                quickCleanButton.IsEnabled = true;
            }
        }

        private bool EmptyRecycleBin()
        {
            logger.Log("Emptying Recycle Bin...");

            var result = PowerShellHelper.Run(@"
try {
    Clear-RecycleBin -Force -ErrorAction Stop
    'RECYCLE_BIN_CLEARED'
}
catch {
    $message = $_.Exception.Message
    if ($message -match 'empty' -or $message -match 'cannot find' -or $message -match 'does not exist') {
        'RECYCLE_BIN_ALREADY_EMPTY'
    }
    else {
        'RECYCLE_BIN_WARNING:' + $message
    }
}");

            bool success = result.Success;
            foreach (string line in SplitQuickOutput(result.StdOut))
            {
                if (line.Equals("RECYCLE_BIN_CLEARED", StringComparison.OrdinalIgnoreCase))
                {
                    logger.Log("Recycle Bin emptied.");
                    success = true;
                }
                else if (line.Equals("RECYCLE_BIN_ALREADY_EMPTY", StringComparison.OrdinalIgnoreCase))
                {
                    logger.Log("Recycle Bin was already empty.");
                    success = true;
                }
                else if (line.StartsWith("RECYCLE_BIN_WARNING:", StringComparison.OrdinalIgnoreCase))
                {
                    logger.Log("WARNING: Recycle Bin could not be completely emptied: " + line.Substring("RECYCLE_BIN_WARNING:".Length).Trim());
                    success = false;
                }
            }

            if (!string.IsNullOrWhiteSpace(result.StdErr))
            {
                logger.Log("WARNING: " + result.StdErr.Trim());
                success = false;
            }

            return success;
        }

        private static string[] SplitQuickOutput(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToArray();
        }
    }
}
