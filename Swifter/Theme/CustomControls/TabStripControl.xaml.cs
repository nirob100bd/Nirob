using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Swifter.Theme.CustomControls;

public class TabItemModel : INotifyPropertyChanged
{
    private string _title;
    private string _url;
    private string _favicon;
    private bool _isActive;
    private bool _isLoading;
    private bool _isMuted;
    private bool _isPinned;
    private string _groupId;
    private bool _isGroupCollapsed;
    private int _audioIndicatorVisibility;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }
    public string Url { get => _url; set { _url = value; OnPropertyChanged(); } }
    public string Favicon { get => _favicon; set { _favicon = value; OnPropertyChanged(); } }
    public bool IsActive { get => _isActive; set { _isActive = value; OnPropertyChanged(); } }
    public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }
    public bool IsMuted { get => _isMuted; set { _isMuted = value; OnPropertyChanged(); AudioIndicatorVisibility = value ? 0 : 1; } }
    public bool IsPinned { get => _isPinned; set { _isPinned = value; OnPropertyChanged(); } }
    public string GroupId { get => _groupId; set { _groupId = value; OnPropertyChanged(); } }
    public bool IsGroupCollapsed { get => _isGroupCollapsed; set { _isGroupCollapsed = value; OnPropertyChanged(); } }
    public int AudioIndicatorVisibility { get => _audioIndicatorVisibility; set { _audioIndicatorVisibility = value; OnPropertyChanged(); } }
    public event PropertyChangedEventHandler PropertyChanged;
    public void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class TabStripControl : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty TabsProperty = DependencyProperty.Register(
        nameof(Tabs), typeof(ObservableCollection<TabItemModel>), typeof(TabStripControl),
        new PropertyMetadata(null, OnTabsChanged));

    public static readonly DependencyProperty IsVerticalProperty = DependencyProperty.Register(
        nameof(IsVertical), typeof(bool), typeof(TabStripControl),
        new PropertyMetadata(false, OnLayoutChanged));

    public event EventHandler<TabItemModel> TabActivated;
    public event EventHandler<TabItemModel> TabCloseRequested;
    public event EventHandler<TabItemModel> TabNewRequested;
    public event EventHandler NewTabRequested;
    public event EventHandler BackRequested;
    public event EventHandler ForwardRequested;
    public event EventHandler ReloadRequested;
    public event EventHandler HomeRequested;
    public event EventHandler MenuRequested;
    public event EventHandler<bool> VerticalLayoutToggled;
    public event EventHandler<bool> SplitToggled;
    public event PropertyChangedEventHandler PropertyChanged;

    public ObservableCollection<TabItemModel> Tabs
    {
        get => (ObservableCollection<TabItemModel>)GetValue(TabsProperty);
        set => SetValue(TabsProperty, value);
    }

    public bool IsVertical
    {
        get => (bool)GetValue(IsVerticalProperty);
        set => SetValue(IsVerticalProperty, value);
    }

    public Brush TextSecondaryBrush => App.ThemeManager?.IsDark == true ? new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)) : new SolidColorBrush(Color.FromRgb(0x5C, 0x5C, 0x5C));
    public Brush TabActiveBrush => App.ThemeManager?.IsDark == true ? new SolidColorBrush(Color.FromRgb(0x32, 0x32, 0x32)) : new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    public Brush TextPrimaryBrush => App.ThemeManager?.IsDark == true ? new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)) : new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
    public Brush AccentBrush => new SolidColorBrush(App.AccentProvider?.CurrentAccent ?? Color.FromRgb(0x00, 0x78, 0xD4));

    public TabStripControl()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (s, e) => RebuildTabs();
    }

    private static void OnTabsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (TabStripControl)d;
        if (e.OldValue is ObservableCollection<TabItemModel> old)
            old.CollectionChanged -= ctrl.OnTabsCollectionChanged;
        if (e.NewValue is ObservableCollection<TabItemModel> neo)
            neo.CollectionChanged += ctrl.OnTabsCollectionChanged;
        ctrl.RebuildTabs();
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((TabStripControl)d).RebuildTabs();
    }

    private void OnTabsCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.Invoke(RebuildTabs);
    }

    private void RebuildTabs()
    {
        if (TabContainer == null || VerticalTabContainer == null) return;
        TabContainer.Children.Clear();
        VerticalTabContainer.Children.Clear();
        var tabs = Tabs;
        if (tabs == null) return;
        foreach (var tab in tabs)
        {
            if (IsVertical)
            {
                VerticalTabContainer.Children.Add(BuildVerticalTab(tab));
            }
            else
            {
                if (!string.IsNullOrEmpty(tab.GroupId))
                {
                    var groupHeader = tabs.FirstOrDefault(t => t.GroupId == tab.GroupId && t.Id.ToString() == tab.GroupId);
                    TabContainer.Children.Add(BuildHorizontalTab(tab));
                }
                else
                {
                    TabContainer.Children.Add(BuildHorizontalTab(tab));
                }
            }
        }
        TabPanelBorder.Visibility = IsVertical ? Visibility.Visible : Visibility.Collapsed;
        VerticalTabContainer.Visibility = IsVertical ? Visibility.Visible : Visibility.Collapsed;
    }

    private UIElement BuildHorizontalTab(TabItemModel tab)
    {
        var grid = new Grid { Width = 200, Height = 28, Margin = new Thickness(2, 3, 2, 3), Cursor = Cursors.Hand };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var border = new Border
        {
            CornerRadius = new CornerRadius(App.ThemeManager?.Settings.TabRadius ?? 8),
            Background = tab.IsActive ? TabActiveBrush : Brushes.Transparent,
            BorderBrush = tab.IsActive ? AccentBrush : Brushes.Transparent,
            BorderThickness = new Thickness(tab.IsActive ? 1.5 : 0),
            Padding = new Thickness(8, 0, 4, 0)
        };

        var title = new TextBlock
        {
            Text = tab.Title ?? "New Tab",
            FontSize = 12,
            Foreground = TextPrimaryBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(6, 0, 0, 0)
        };
        Grid.SetColumn(title, 1);

        var spinner = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = AccentBrush,
            Visibility = tab.IsLoading ? Visibility.Visible : Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
            Stroke = AccentBrush,
            StrokeThickness = 1.5
        };
        Grid.SetColumn(spinner, 0);

        var audio = new TextBlock
        {
            Text = tab.IsMuted ? "🔇" : "🔊",
            FontSize = 10,
            Visibility = tab.AudioIndicatorVisibility == 0 ? Visibility.Visible : Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        Grid.SetColumn(audio, 1);

        var close = new Button
        {
            Style = (Style)FindResource("TabCloseBtn"),
            Tag = tab,
            VerticalAlignment = VerticalAlignment.Center
        };
        close.Click += OnTabCloseClick;
        Grid.SetColumn(close, 2);

        var inner = new Grid();
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var favicon = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = AccentBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(favicon, 0);
        inner.Children.Add(favicon);
        inner.Children.Add(title);
        inner.Children.Add(close);
        border.Child = inner;

        grid.Children.Add(border);
        var fe = (FrameworkElement)grid;
        fe.DataContext = tab;
        grid.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 2) { TabNewRequested?.Invoke(this, EventArgs.Empty); return; }
            TabActivated?.Invoke(this, tab);
            e.Handled = true;
        };
        grid.MouseMiddleButtonUp += (s, e) => { TabCloseRequested?.Invoke(this, tab); e.Handled = true; };
        grid.ToolTip = tab.Url;
        return grid;
    }

    private UIElement BuildVerticalTab(TabItemModel tab)
    {
        var border = new Border
        {
            Height = 34,
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(App.ThemeManager?.Settings.TabRadius ?? 8),
            Background = tab.IsActive ? TabActiveBrush : Brushes.Transparent,
            BorderBrush = tab.IsActive ? AccentBrush : Brushes.Transparent,
            BorderThickness = new Thickness(tab.IsActive ? 1.5 : 0),
            Padding = new Thickness(8, 0, 4, 0),
            Cursor = Cursors.Hand
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var favicon = new Ellipse { Width = 12, Height = 12, Fill = AccentBrush, VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock
        {
            Text = tab.Title ?? "New Tab",
            FontSize = 12,
            Foreground = TextPrimaryBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(title, 1);
        var close = new Button { Style = (Style)FindResource("TabCloseBtn"), Tag = tab, Visibility = tab.IsActive ? Visibility.Visible : Visibility.Collapsed };
        close.Click += OnTabCloseClick;
        Grid.SetColumn(close, 2);
        grid.Children.Add(favicon);
        grid.Children.Add(title);
        grid.Children.Add(close);
        border.Child = grid;
        border.MouseLeftButtonDown += (s, e) => { TabActivated?.Invoke(this, tab); e.Handled = true; };
        border.MouseMiddleButtonUp += (s, e) => { TabCloseRequested?.Invoke(this, tab); e.Handled = true; };
        border.ToolTip = tab.Url;
        return border;
    }

    private void OnTabCloseClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TabItemModel tab)
        {
            TabCloseRequested?.Invoke(this, tab);
            e.Handled = true;
        }
    }

    private void OnNewTabClick(object sender, RoutedEventArgs e) => NewTabRequested?.Invoke(this, EventArgs.Empty);
    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private void OnForwardClick(object sender, RoutedEventArgs e) => ForwardRequested?.Invoke(this, EventArgs.Empty);
    private void OnReloadClick(object sender, RoutedEventArgs e) => ReloadRequested?.Invoke(this, EventArgs.Empty);
    private void OnHomeClick(object sender, RoutedEventArgs e) => HomeRequested?.Invoke(this, EventArgs.Empty);
    private void OnMenuClick(object sender, RoutedEventArgs e) => MenuRequested?.Invoke(this, EventArgs.Empty);

    private void OnToggleVerticalLayout(object sender, RoutedEventArgs e)
    {
        var on = VerticalLayoutToggle.IsChecked == true;
        VerticalLayoutToggled?.Invoke(this, on);
    }

    private void OnToggleSplit(object sender, RoutedEventArgs e)
    {
        var on = SplitToggle.IsChecked == true;
        SplitToggled?.Invoke(this, on);
    }

    public void SetSplitChecked(bool value)
    {
        if (SplitToggle != null) SplitToggle.IsChecked = value;
    }

    public void SetVerticalChecked(bool value)
    {
        if (VerticalLayoutToggle != null) VerticalLayoutToggle.IsChecked = value;
    }

    public void NotifyThemeChanged()
    {
        OnPropertyChanged(nameof(TextSecondaryBrush));
        OnPropertyChanged(nameof(TabActiveBrush));
        OnPropertyChanged(nameof(TextPrimaryBrush));
        OnPropertyChanged(nameof(AccentBrush));
        RebuildTabs();
    }

    protected void OnPropertyChanged([CallerMemberName] string name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
