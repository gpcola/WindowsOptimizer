using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindowsOptimizer
{
    public partial class MainWindow
    {
        private bool quickModeInitialized;
        private Button quickCleanButton = null!;
        private Button quickNetworkButton = null!;
        private Button quickMediaStreamingButton = null!;
        private TextBlock quickCleanStatus = null!;
        private TextBlock quickNetworkStatus = null!;
        private TextBlock quickMediaStreamingStatus = null!;
        private ProgressBar quickProgress = null!;
        private TextBlock quickProgressStatus = null!;
        private TextBlock quickProgressDetail = null!;
        private TabControl modeTabs = null!;

        public void EnableQuickModeShell()
        {
            if (quickModeInitialized || Content is not UIElement advancedContent)
                return;

            Content = null;

            modeTabs = new TabControl
            {
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                SelectedIndex = 0,
                Background = SystemColors.WindowBrush
            };

            var quickTab = new TabItem
            {
                Header = "Simple clean",
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
            Title = "Windows Optimizer — 1LG Digital";

            MinWidth = 760;
            MinHeight = 650;
            Width = 900;
            Height = 760;
            quickModeInitialized = true;

            SetQuickProgress(
                0,
                1,
                "Ready",
                "Edge, browser profiles, Microsoft Store app data and your custom exclusions are protected.",
                false);
        }

        private FrameworkElement BuildQuickCleanContent()
        {
            var root = new Grid
            {
                Background = SystemColors.WindowBrush
            };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var brandBrush = new LinearGradientBrush(
                Color.FromRgb(76, 29, 149),
                Color.FromRgb(37, 99, 235),
                0);

            var header = new Border
            {
                Background = brandBrush,
                Padding = new Thickness(34, 24, 34, 24)
            };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var brandStack = new StackPanel();

            brandStack.Children.Add(new TextBlock
            {
                Text = "1LG DIGITAL",
                Foreground = Brushes.White,
                FontSize = 21,
                FontWeight = FontWeights.Bold,
                CharacterSpacing = 80
            });

            brandStack.Children.Add(new TextBlock
            {
                Text = "Windows Optimizer",
                Foreground = Brushes.White,
                FontSize = 34,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 0, 4)
            });

            brandStack.Children.Add(new TextBlock
            {
                Text = "Safe Windows housekeeping with visible progress and protected application data.",
                Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                FontSize = 14
            });

            headerGrid.Children.Add(brandStack);

            var safeBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(14, 7, 14, 7),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(safeBadge, 1);
            safeBadge.Child = new TextBlock
            {
                Text = "SAFE MODE",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 12
            };
            headerGrid.Children.Add(safeBadge);

            header.Child = headerGrid;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var contentScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(contentScroll, 1);

            var content = new StackPanel
            {
                MaxWidth = 680,
                Margin = new Thickness(32, 28, 32, 24),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var primaryCard = new Border
            {
                Padding = new Thickness(28),
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                BorderBrush = SystemColors.ActiveBorderBrush,
                Background = SystemColors.ControlBrush
            };

            var panel = new StackPanel();
            primaryCard.Child = panel;

            panel.Children.Add(new TextBlock
            {
                Text = "Clean up this PC",
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "One safe action for routine housekeeping.",
                FontSize = 15,
                Opacity = 0.78,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 18)
            });

            var steps = new StackPanel
            {
                Margin = new Thickness(18, 0, 18, 18)
            };

            foreach (string step in new[]
            {
                "✓ Remove stale top-level temporary files only",
                "✓ Empty items already in the Windows Recycle Bin",
                "✓ Clear the Windows Update download cache only when servicing is idle"
            })
            {
                steps.Children.Add(new TextBlock
                {
                    Text = step,
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 0, 8)
                });
            }

            panel.Children.Add(steps);

            quickCleanButton = new Button
            {
                Content = "Run safe cleanup",
                FontSize = 19,
                FontWeight = FontWeights.SemiBold,
                Height = 58,
                MinWidth = 330,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 14)
            };
            quickCleanButton.Click += QuickClean_Click;
            panel.Children.Add(quickCleanButton);

            quickCleanStatus = new TextBlock
            {
                Text = "Application data is never a cleanup target. Use Advanced to review or add protected folders.",
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = 580,
                Opacity = 0.78
            };
            panel.Children.Add(quickCleanStatus);

            content.Children.Add(primaryCard);

            var protectionCard = new Border
            {
                Padding = new Thickness(20),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                BorderBrush = SystemColors.ActiveBorderBrush,
                Background = SystemColors.ControlBrush,
                Margin = new Thickness(0, 14, 0, 0)
            };

            protectionCard.Child = new TextBlock
            {
                Text = "Protected by default: Microsoft Edge user data, Chrome/Brave/Firefox profiles, Microsoft Store app data, Windows identity stores and any folders you add in Advanced.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Opacity = 0.82
            };

            content.Children.Add(protectionCard);
            contentScroll.Content = content;
            root.Children.Add(contentScroll);

            var progressCard = new Border
            {
                Background = SystemColors.ControlBrush,
                BorderBrush = SystemColors.ActiveBorderBrush,
                BorderThickness = new Thickness(1, 1, 1, 0),
                Padding = new Thickness(28, 16, 28, 18)
            };

            var progressPanel = new StackPanel
            {
                MaxWidth = 760,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var progressHeader = new Grid();
            progressHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progressHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            quickProgressStatus = new TextBlock
            {
                Text = "Ready",
                FontWeight = FontWeights.SemiBold,
                FontSize = 14
            };
            progressHeader.Children.Add(quickProgressStatus);

            var byline = new TextBlock
            {
                Text = "1LG Digital",
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(91, 33, 182)),
                FontSize = 12
            };
            Grid.SetColumn(byline, 1);
            progressHeader.Children.Add(byline);

            progressPanel.Children.Add(progressHeader);

            quickProgress = new ProgressBar
            {
                Height = 9,
                Minimum = 0,
                Maximum = 1,
                Margin = new Thickness(0, 8, 0, 7)
            };
            progressPanel.Children.Add(quickProgress);

            quickProgressDetail = new TextBlock
            {
                Text = "Waiting for an operation.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.72
            };
            progressPanel.Children.Add(quickProgressDetail);

            progressCard.Child = progressPanel;
            Grid.SetRow(progressCard, 2);
            root.Children.Add(progressCard);

            return root;
        }

        private void ModeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, modeTabs))
                return;

            if (modeTabs.SelectedIndex == 1)
            {
                MinWidth = 1000;
                MinHeight = 720;
                if (Width < 1220) Width = 1220;
                if (Height < 860) Height = 860;
            }
            else
            {
                MinWidth = 760;
                MinHeight = 650;
                if (Width > 980) Width = 900;
                if (Height > 820) Height = 760;
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
            quickCleanStatus.Text = "Cleaning safely. Protected, active or uncertain items will be skipped.";

            var actions = new List<(string Name, Func<bool> Execute)>
            {
                ("Clean stale temp files", optimizer.CleanTempFiles),
                ("Empty Recycle Bin", optimizer.EmptyRecycleBin),
                ("Clear Windows Update cache", optimizer.ClearUpdateCache)
            };

            try
            {
                await RunWorkflowAsync(actions, "Safe cleanup");
                quickCleanStatus.Text =
                    "Finished. Browser profiles, app data, Windows features, indexing settings and unrelated services were not touched.";
            }
            catch (Exception ex)
            {
                quickCleanStatus.Text =
                    "Safe cleanup stopped after an error. Open Advanced to review the activity log.";
                logger.Log("ERR: Quick clean: " + ex.Message);
            }
            finally
            {
                quickCleanButton.IsEnabled = true;
            }
        }

        private void SetQuickProgress(
            int completed,
            int total,
            string status,
            string detail,
            bool active)
        {
            if (!quickModeInitialized || quickProgress == null)
                return;

            quickProgress.IsIndeterminate = active && total <= 0;

            if (total > 0)
            {
                quickProgress.IsIndeterminate = false;
                quickProgress.Minimum = 0;
                quickProgress.Maximum = total;
                quickProgress.Value = Math.Clamp(completed, 0, total);
            }
            else if (!active)
            {
                quickProgress.Value = 0;
            }

            quickProgressStatus.Text = status;
            quickProgressDetail.Text = detail;
        }

        private void SetQuickNetworkStatus(string text)
        {
            if (quickModeInitialized && quickNetworkStatus != null)
                quickNetworkStatus.Text = text;
        }

        private void SetQuickNetworkButtonEnabled(bool enabled)
        {
            if (quickModeInitialized && quickNetworkButton != null)
                quickNetworkButton.IsEnabled = enabled;
        }

        private void SetQuickMediaStreamingStatus(string text)
        {
            if (quickModeInitialized && quickMediaStreamingStatus != null)
                quickMediaStreamingStatus.Text = text;
        }

        private void SetQuickMediaStreamingButtonEnabled(bool enabled)
        {
            if (quickModeInitialized && quickMediaStreamingButton != null)
                quickMediaStreamingButton.IsEnabled = enabled;
        }
    }
}
