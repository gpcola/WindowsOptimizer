using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

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
        private TabControl modeTabs = null!;

        public void EnableQuickModeShell()
        {
            if (quickModeInitialized || Content is not UIElement advancedContent)
                return;

            Content = null;

            modeTabs = new TabControl
            {
                Margin = new Thickness(12),
                SelectedIndex = 0
            };

            var quickTab = new TabItem
            {
                Header = "Simple",
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
            Title = "Windows Optimizer by 1LG Digital";

            MinWidth = 700;
            MinHeight = 650;
            Width = 820;
            Height = 760;
            quickModeInitialized = true;
            RefreshNetworkStatus();
            RefreshMediaStreamingStatus();
        }

        private FrameworkElement BuildQuickCleanContent()
        {
            var root = new Grid
            {
                Margin = new Thickness(24),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var card = new Border
            {
                MaxWidth = 660,
                Padding = new Thickness(28),
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
                Text = "1LG DIGITAL",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(91, 33, 182)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Windows Optimizer",
                FontSize = 28,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Safe housekeeping and performance maintenance for Windows.",
                FontSize = 16,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            });

            quickCleanButton = new Button
            {
                Content = "Clean up this PC",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Height = 60,
                MinWidth = 320,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            quickCleanButton.Click += QuickClean_Click;
            panel.Children.Add(quickCleanButton);

            quickCleanStatus = new TextBlock
            {
                Text = "Cleans only stale temporary files, the Recycle Bin and an idle Windows Update download cache. No apps or Windows features are removed.",
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = 580,
                Opacity = 0.8,
                Margin = new Thickness(0, 0, 0, 18)
            };
            panel.Children.Add(quickCleanStatus);

            quickNetworkButton = new Button
            {
                Content = "Optimize Ethernet & Wi-Fi",
                FontSize = 15,
                Height = 46,
                MinWidth = 320,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            quickNetworkButton.Click += OptimizeNetwork_Click;
            panel.Children.Add(quickNetworkButton);

            quickNetworkStatus = new TextBlock
            {
                Text = "Uses supported Windows high-throughput settings on physical adapters only. PIA VPN settings are preserved.",
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = 580,
                Opacity = 0.8,
                Margin = new Thickness(0, 0, 0, 18)
            };
            panel.Children.Add(quickNetworkStatus);

            quickMediaStreamingButton = new Button
            {
                Content = "Make this PC a LAN media streamer",
                FontSize = 15,
                Height = 46,
                MinWidth = 320,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            quickMediaStreamingButton.Click += EnableMediaStreaming_Click;
            panel.Children.Add(quickMediaStreamingButton);

            quickMediaStreamingStatus = new TextBlock
            {
                Text = "Media streaming is only enabled on a trusted private LAN. If PIA is connected, Allow LAN Traffic should be enabled in PIA.",
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = 580,
                Opacity = 0.8,
                Margin = new Thickness(0, 0, 0, 18)
            };
            panel.Children.Add(quickMediaStreamingStatus);

            panel.Children.Add(new TextBlock
            {
                Text = "A 1LG Digital utility",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.75
            });

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
                MinWidth = 980;
                MinHeight = 700;
                if (Width < 1180) Width = 1180;
                if (Height < 840) Height = 840;
            }
            else
            {
                MinWidth = 700;
                MinHeight = 650;
                if (Width > 920) Width = 820;
                if (Height > 800) Height = 760;
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
                ("Clean stale temp files", optimizer.CleanTempFiles),
                ("Empty Recycle Bin", optimizer.EmptyRecycleBin),
                ("Clear Windows Update cache", optimizer.ClearUpdateCache)
            };

            try
            {
                await RunWorkflowAsync(actions, "Quick clean");
                quickCleanStatus.Text =
                    "Finished. No apps, Windows features, services, indexing settings or application data were changed.";
            }
            catch (Exception ex)
            {
                quickCleanStatus.Text =
                    "Quick clean stopped safely after an error. Open Advanced to review the activity log.";
                logger.Log("ERR: Quick clean: " + ex.Message);
            }
            finally
            {
                quickCleanButton.IsEnabled = true;
            }
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
