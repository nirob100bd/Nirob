using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Swifter.UI.ViewModels;

namespace Swifter.UI.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    private readonly FrameworkElement[] _panels;

    public SettingsWindow(SettingsViewModel viewModel = null)
    {
        _vm = viewModel ?? new SettingsViewModel();
        InitializeComponent();
        DataContext = _vm;
        _panels = new FrameworkElement[]
        {
            BuildAppearance(),
            BuildPerformance(),
            BuildPrivacy(),
            BuildDownloads(),
            BuildShortcuts(),
            BuildContainers(),
            BuildExtensions(),
            BuildAbout()
        };
        _vm.StatusRequested += msg => Dispatcher.Invoke(() => StatusLabel.Text = msg);
        _vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.IsDirty))
                StatusLabel.Text = _vm.IsDirty ? "Unsaved changes" : "No unsaved changes";
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ShowPanel(0);
        UsageLine.Text = _vm.GetUsageSummary();
    }

    private void OnNavSelection(object sender, SelectionChangedEventArgs e)
    {
        ShowPanel(NavList.SelectedIndex);
    }

    private void ShowPanel(int index)
    {
        if (index < 0 || index >= _panels.Length) return;
        PanelHost.Content = _panels[index];
    }

    private FrameworkElement BuildAppearance()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 24, 24) };
        panel.Children.Add(Heading("Appearance"));
        panel.Children.Add(SetRow("Theme", MakeCombo(new[] { "System", "Light", "Dark" }, idx => _vm.Theme = (Theme.AppTheme)idx, (int)_vm.Theme)));
        panel.Children.Add(SetRow("Accent color", MakeColorBox(_vm.AccentHex, v => _vm.AccentHex = v)));
        panel.Children.Add(SetRow("Mica backdrop", MakeToggle(_vm.MicaEnabled, v => _vm.MicaEnabled = v)));
        panel.Children.Add(SetRow("Tab corner radius", MakeSlider(0, 16, _vm.TabRadius, v => _vm.TabRadius = v)));
        panel.Children.Add(SetRow("Vertical tab sidebar", MakeToggle(_vm.VerticalTabs, v => _vm.VerticalTabs = v)));
        panel.Children.Add(SetRow("Restore last session", MakeToggle(_vm.RestoreSession, v => _vm.RestoreSession = v)));
        panel.Children.Add(SetRow("Custom wallpaper", MakeButton("Choose image…", _ => _vm.WallpaperRequested?.Invoke())));
        return panel;
    }

    private FrameworkElement BuildPerformance()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 24, 24) };
        panel.Children.Add(Heading("Performance"));
        panel.Children.Add(SetRow("Low-end CPU throttle", MakeToggle(_vm.LowEndMode, v => _vm.LowEndMode = v)));
        panel.Children.Add(SetRow("Memory trim threshold", MakeCombo(new[] { "100 MB", "200 MB", "400 MB", "800 MB" }, idx => _vm.MemThresholdMb = new[] { 100, 200, 400, 800 }[idx], Math.Max(0, new[] { 100, 200, 400, 800 }.ToList().IndexOf(_vm.MemThresholdMb)))));
        panel.Children.Add(SetRow("Tab hibernation", MakeToggle(_vm.HibernationEnabled, v => _vm.HibernationEnabled = v)));
        panel.Children.Add(SetRow("Trim working set now", MakeButton("🧠 Trim now", _ => _vm.TrimRequested?.Invoke())));
        var stats = new TextBlock
        {
            Text = _vm.GetUsageSummary(),
            FontSize = 12,
            Opacity = 0.75,
            Margin = new Thickness(4, 18, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(stats);
        return panel;
    }

    private FrameworkElement BuildPrivacy()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 24, 24) };
        panel.Children.Add(Heading("Privacy"));
        var shield = App.AdBlock;
        panel.Children.Add(SetRow("Ad / tracker blocking", MakeToggle(shield?.ShieldEnabled != false, v => { if (shield != null) shield.ShieldEnabled = v; })));
        panel.Children.Add(SetRow("Aggressive mode", MakeToggle(shield?.AggressiveMode == true, v => { if (shield != null) shield.AggressiveMode = v; })));
        panel.Children.Add(SetRow("Tracking parameter strip", MakeToggle(App.ParamStripper?.Enabled != false, v => { if (App.ParamStripper != null) App.ParamStripper.Enabled = v; })));
        var fp = App.FingerprintProtector;
        panel.Children.Add(SetRow("Canvas noise", MakeToggle(fp?.CanvasNoise != false, v => { if (fp != null) fp.CanvasNoise = v; })));
        panel.Children.Add(SetRow("Audio noise", MakeToggle(fp?.AudioNoise != false, v => { if (fp != null) fp.AudioNoise = v; })));
        panel.Children.Add(SetRow("WebGL spoof", MakeToggle(fp?.WebGLSpoof != false, v => { if (fp != null) fp.WebGLSpoof = v; })));
        panel.Children.Add(SetRow("WebRTC leak guard", MakeToggle(App.WebRtcProtection?.BlockNonProxiedUdp != false, v => { if (App.WebRtcProtection != null) App.WebRtcProtection.BlockNonProxiedUdp = v; })));
        panel.Children.Add(SetRow("Clear browsing data", MakeButton("Clear now…", _ => _vm.ClearDataRequested?.Invoke(), true)));
        return panel;
    }

    private FrameworkElement BuildDownloads()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 24, 24) };
        panel.Children.Add(Heading("Downloads"));
        panel.Children.Add(SetRow("Download folder", new TextBox
        {
            Text = _vm.DownloadFolder,
            IsReadOnly = true,
            Width = 320,
            VerticalAlignment = VerticalAlignment.Center
        }));
        panel.Children.Add(SetRow("Concurrent segments", MakeCombo(new[] { "4", "8", "16" }, idx => _vm.Segments = new[] { 4, 8, 16 }[idx], _vm.Segments == 4 ? 0 : _vm.Segments == 8 ? 1 : 2)));
        panel.Children.Add(SetRow("Global speed limit", MakeCombo(new[] { "Unlimited", "1 MB/s", "5 MB/s", "10 MB/s" }, idx => _vm.GlobalSpeedLimit = new long[] { 0, 1024, 5120, 10240 }[idx], 0)));
        panel.Children.Add(SetRow("Auto-sniff media streams", MakeToggle(_vm.AutoSniff, v => _vm.AutoSniff = v)));
        panel.Children.Add(SetRow("Video overlay button", MakeToggle(_vm.VideoOverlay, v => _vm.VideoOverlay = v)));
        return panel;
    }

    private FrameworkElement BuildShortcuts()
    {
        var wrap = new StackPanel { Margin = new Thickness(0, 0, 24, 24) };
        wrap.Children.Add(Heading("Keyboard Shortcuts"));
        wrap.Children.Add(SetRow("Mouse gestures", MakeToggle(_vm.GestureEnabled, v => _vm.GestureEnabled = v)));
        var grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        var header = new Border { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4)), Padding = new Thickness(10, 7, 10, 7), CornerRadius = new CornerRadius(8, 8, 0, 0) };
        var hg = new Grid();
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        foreach (var (label, col) in new[] { ("Action", 0), ("Gesture", 1), ("On", 2) })
        {
            var tb = new TextBlock { Text = label, Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 12 };
            Grid.SetColumn(tb, col);
            hg.Children.Add(tb);
        }
        header.Child = hg;
        Grid.SetRow(header, 0);
        grid.Children.Add(header);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var row = 1;
        foreach (var h in _vm.Hotkeys)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Children.Add(Cell(h.Description, row, 0));
            var gestureBox = new TextBox
            {
                Text = h.Gesture,
                Margin = new Thickness(4),
                Padding = new Thickness(6, 4, 6, 4)
            };
            gestureBox.LostFocus += (s, e) => App.Hotkeys?.SetGesture(h.Id, gestureBox.Text);
            Grid.SetRow(gestureBox, row);
            Grid.SetColumn(gestureBox, 1);
            grid.Children.Add(gestureBox);
            var enabled = new CheckBox
            {
                IsChecked = h.Enabled,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4)
            };
            enabled.Checked += (s, e) => App.Hotkeys?.SetEnabled(h.Id, true);
            enabled.Unchecked += (s, e) => App.Hotkeys?.SetEnabled(h.Id, false);
            Grid.SetRow(enabled, row);
            Grid.SetColumn(enabled, 2);
            grid.Children.Add(enabled);
            row++;
        }
        wrap.Children.Add(grid);
        wrap.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 14, 0, 0),
            Children =
            {
                MakeButton("Reset defaults", _ => { App.Hotkeys?.ResetDefaults(); StatusLabel.Text = "Shortcuts reset"; }),
                new TextBlock
                {
                    Text = "  Gestures: L=Back · R=Forward · D=Close · U=New Tab · UR=Open link",
                    FontSize = 12,
                    Opacity = 0.7,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        });
        return wrap;
    }

    private FrameworkElement BuildContainers()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 24, 24) };
        panel.Children.Add(Heading("Container Profiles"));
        panel.Children.Add(new TextBlock
        {
            Text = "Isolated CoreWebView2Profile instances with independent cookie jars and local storage.",
            FontSize = 12,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 14),
            TextWrapping = TextWrapping.Wrap
        });
        var items = new ItemsControl { ItemsSource = _vm.Containers };
        panel.Children.Add(items);
        panel.Children.Add(MakeButton("＋ New container", _ => _vm.NewContainerCommand.Execute(null)));
        return panel;
    }

    private FrameworkElement BuildExtensions()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 24, 24) };
        panel.Children.Add(Heading("Extensions (Manifest V3)"));
        if (_vm.Extensions.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No extensions installed.",
                Opacity = 0.7,
                Margin = new Thickness(0, 8, 0, 0)
            });
        }
        panel.Children.Add(new ItemsControl { ItemsSource = _vm.Extensions });
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 14, 0, 0),
            Children =
            {
                MakeButton("Load unpacked…", _ =>
                {
                    var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select extension folder" };
                    if (dlg.ShowDialog() == true)
                    {
                        var ext = App.ExtensionHost?.InstallFromDirectory(dlg.FolderName);
                        StatusLabel.Text = ext != null ? "Installed " + ext.Name : "Install failed — check manifest.json (V3)";
                    }
                }),
                MakeButton("Reload all", _ => App.ExtensionHost?.ReloadAll())
            }
        });
        return panel;
    }

    private FrameworkElement BuildAbout()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 24, 24) };
        panel.Children.Add(Heading("About Swifter"));
        var info = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 260,
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Code, Consolas"),
            FontSize = 12,
            Text =
                "Swifter Browser " + Program.GetVersionString() + Environment.NewLine +
                "Runtime: .NET 8 / WPF / WebView2" + Environment.NewLine +
                "Target: " + Environment.OSVersion.VersionString + Environment.NewLine +
                "CPU threads: " + Environment.ProcessorCount + Environment.NewLine +
                "User data: " + Program.GetUserDataRoot() + Environment.NewLine +
                "Database: " + Program.GetDatabasePath() + Environment.NewLine + Environment.NewLine +
                "Subsystems:" + Environment.NewLine +
                "  • EasyList / AdGuard trie shields + cosmetic hider" + Environment.NewLine +
                "  • 16-segment resumable download engine with bandwidth throttle" + Environment.NewLine +
                "  • HLS/DASH media sniffer + injected video overlay" + Environment.NewLine +
                "  • Split screen, vertical tabs, tab groups, hibernation" + Environment.NewLine +
                "  • AES-250-GCM password vault + autofill templates" + Environment.NewLine +
                "  • MV3 extension host with background service workers" + Environment.NewLine +
                "  • swifter:// protocol pages (newtab, downloads, history, …)" + Environment.NewLine +
                "  • DWM Mica / Acrylic backdrop, mouse gestures, command palette"
        };
        panel.Children.Add(info);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 14, 0, 0),
            Children =
            {
                MakeButton("Open data directory", _ =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = Program.GetUserDataRoot(),
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }),
                MakeButton("Export passwords", _ =>
                {
                    var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "JSON|*.json", FileName = "swifter-passwords.json" };
                    if (dlg.ShowDialog() == true)
                    {
                        try { App.Autofill?.ExportJson(dlg.FileName, ""); StatusLabel.Text = "Passwords exported"; }
                        catch (Exception ex) { StatusLabel.Text = "Export failed: " + ex.Message; }
                    }
                })
            }
        });
        return panel;
    }

    private static TextBlock Heading(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 16)
        };
    }

    private static UIElement Cell(string text, int row, int col)
    {
        var tb = new TextBlock
        {
            Text = text ?? "",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 7, 10, 7),
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        return tb;
    }

    private static UIElement SetRow(string label, UIElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        var tb = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14
        };
        Grid.SetColumn(tb, 0);
        var host = new ContentControl
        {
            Content = control,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(host, 1);
        grid.Children.Add(tb);
        grid.Children.Add(host);
        return grid;
    }

    private static ToggleButton MakeToggle(bool value, Action<bool> onChange)
    {
        var t = new ToggleButton
        {
            IsChecked = value,
            Width = 44,
            Height = 24,
            Focusable = true
        };
        t.Style = BuildToggleStyle();
        t.Checked += (s, e) => onChange(true);
        t.Unchecked += (s, e) => onChange(false);
        return t;
    }

    private static Style BuildToggleStyle()
    {
        var style = new Style(typeof(ToggleButton));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0))));
        style.Setters.Add(new Setter(Control.TemplateProperty, BuildToggleTemplate()));
        return style;
    }

    private static ControlTemplate BuildToggleTemplate()
    {
        var template = new ControlTemplate(typeof(ToggleButton));
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Control.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new Thickness(12));
        var circle = new FrameworkElementFactory(typeof(Ellipse), "Knob");
        circle.SetValue(FrameworkElement.WidthProperty, 18.0);
        circle.SetValue(FrameworkElement.HeightProperty, 18.0);
        circle.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        circle.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        circle.SetValue(FrameworkElement.MarginProperty, new Thickness(3, 0, 0, 0));
        circle.SetValue(Shape.FillProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White));
        var panel = new FrameworkElementFactory(typeof(Grid));
        panel.AppendChild(circle);
        border.AppendChild(panel);
        template.VisualTree = border;
        var trigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        trigger.Setters.Add(new Setter(Control.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4)), "Bd"));
        trigger.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right, "Knob"));
        template.Triggers.Add(trigger);
        return template;
    }

    private static ComboBox MakeCombo(string[] items, Action<int> onChange, int selected)
    {
        var combo = new ComboBox { Width = 200, ItemsSource = items };
        combo.SelectedIndex = Math.Clamp(selected, 0, items.Length - 1);
        combo.SelectionChanged += (s, e) => { if (combo.SelectedIndex >= 0) onChange(combo.SelectedIndex); };
        return combo;
    }

    private static TextBox MakeColorBox(string value, Action<string> onChange)
    {
        var box = new TextBox { Width = 110, Text = value ?? "#0078D4" };
        box.LostFocus += (s, e) => onChange(box.Text);
        return box;
    }

    private static Slider MakeSlider(double min, double max, double value, Action<double> onChange)
    {
        var slider = new Slider
        {
            Width = 220,
            Minimum = min,
            Maximum = max,
            Value = value,
            VerticalAlignment = VerticalAlignment.Center
        };
        slider.ValueChanged += (s, e) => onChange(e.NewValue);
        return slider;
    }

    private Button MakeButton(string label, Action onClick, bool danger = false)
    {
        var btn = new Button
        {
            Content = label,
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontWeight = FontWeights.SemiBold
        };
        if (danger)
            btn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC4, 0x2B, 0x1C));
        else
            btn.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
        btn.Foreground = System.Windows.Media.Brushes.White;
        btn.Click += (s, e) => onClick();
        return btn;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _vm.Save();
        StatusLabel.Text = "Settings saved";
        UsageLine.Text = _vm.GetUsageSummary();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        _vm.LoadFromSettings();
        StatusLabel.Text = "Reverted to saved settings";
    }

    private void OnClearData(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Delete browsing history, cache, cookies, and saved passwords?\n\nThis cannot be undone.",
            "Clear browsing data",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            _vm.ClearData(true, true, true, true);
            StatusLabel.Text = "Browsing data cleared";
            UsageLine.Text = _vm.GetUsageSummary();
        }
    }
}
