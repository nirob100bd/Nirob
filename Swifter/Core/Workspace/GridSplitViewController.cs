using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Swifter.Core.Workspace;

public sealed class SplitSlot : INotifyPropertyChanged
{
    private Guid _tabId;
    private string _title;
    private string _url;
    private bool _isActive;

    public int Index { get; set; }
    public Guid TabId { get => _tabId; set { _tabId = value; OnPropertyChanged(); } }
    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }
    public string Url { get => _url; set { _url = value; OnPropertyChanged(); } }
    public bool IsActive { get => _isActive; set { _isActive = value; OnPropertyChanged(); } }
    public event PropertyChangedEventHandler PropertyChanged;
    public void OnPropertyChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class GridSplitViewController : INotifyPropertyChanged
{
    private SplitMode _mode = SplitMode.None;
    private int _activeSlot;

    public SplitSlot[] Slots { get; }
    public SplitMode Mode
    {
        get => _mode;
        set { _mode = value; OnPropertyChanged(); OnPropertyChanged(nameof(SlotCount)); OnPropertyChanged(nameof(IsColumns)); OnPropertyChanged(nameof(IsGrid)); }
    }
    public int SlotCount => Mode switch
    {
        SplitMode.Columns2 => 2,
        SplitMode.Grid2x2 => 4,
        _ => 1
    };
    public bool IsColumns => Mode == SplitMode.Columns2;
    public bool IsGrid => Mode == SplitMode.Grid2x2;
    public int ActiveSlot
    {
        get => _activeSlot;
        set { _activeSlot = value; OnPropertyChanged(); SyncActive(); }
    }

    public event Action<SplitMode> ModeChanged;
    public event Action<int, Guid> SlotActivated;
    public event Action<SplitSlot> SlotChanged;
    public event PropertyChangedEventHandler PropertyChanged;

    public GridSplitViewController()
    {
        Slots = new SplitSlot[4];
        for (var i = 0; i < 4; i++)
        {
            Slots[i] = new SplitSlot { Index = i, IsActive = i == 0 };
            Slots[i].PropertyChanged += (s, e) => { if (e.PropertyName == nameof(SplitSlot.TabId)) SlotChanged?.Invoke(Slots[i]); };
        }
    }

    public void SetMode(SplitMode mode)
    {
        Mode = mode;
        if (ActiveSlot >= SlotCount) ActiveSlot = 0;
        ModeChanged?.Invoke(mode);
    }

    public void Cycle()
    {
        SetMode(Mode switch
        {
            SplitMode.None => SplitMode.Columns2,
            SplitMode.Columns2 => SplitMode.Grid2x2,
            _ => SplitMode.None
        });
    }

    public void AssignSlot(int index, Guid tabId, string title = null, string url = null)
    {
        if (index < 0 || index >= Slots.Length) return;
        Slots[index].TabId = tabId;
        if (title != null) Slots[index].Title = title;
        if (url != null) Slots[index].Url = url;
    }

    public void ClearSlot(int index)
    {
        if (index < 0 || index >= Slots.Length) return;
        Slots[index].TabId = Guid.Empty;
        Slots[index].Title = null;
        Slots[index].Url = null;
    }

    public void FocusSlot(int index)
    {
        if (index < 0 || index >= SlotCount) return;
        ActiveSlot = index;
        SlotActivated?.Invoke(index, Slots[index].TabId);
    }

    public void NextSlot()
    {
        if (SlotCount <= 1) return;
        FocusSlot((ActiveSlot + 1) % SlotCount);
    }

    private void SyncActive()
    {
        for (var i = 0; i < Slots.Length; i++) Slots[i].IsActive = i == ActiveSlot;
    }

    public IReadOnlyList<Guid> GetAssignedTabIds()
    {
        return Slots.Take(SlotCount).Select(s => s.TabId).Where(id => id != Guid.Empty).Distinct().ToList();
    }

    public void ArrangeInGrid(Grid host)
    {
        if (host == null) return;
        host.Children.Clear();
        host.ColumnDefinitions.Clear();
        host.RowDefinitions.Clear();
        if (Mode == SplitMode.None) return;

        if (Mode == SplitMode.Columns2)
        {
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < 2; i++)
            {
                var border = BuildSlotBorder(i);
                Grid.SetColumn(border, i * 2);
                host.Children.Add(border);
            }
        }
        else
        {
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < 4; i++)
            {
                var border = BuildSlotBorder(i);
                Grid.SetRow(border, (i / 2) * 2);
                Grid.SetColumn(border, (i % 2) * 2);
                host.Children.Add(border);
            }
        }
    }

    private Border BuildSlotBorder(int index)
    {
        var slot = Slots[index];
        var border = new Border
        {
            Background = System.Windows.Media.Brushes.Transparent,
            BorderBrush = slot.IsActive
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(slot.IsActive ? 2 : 1),
            CornerRadius = new CornerRadius(6),
            Tag = index
        };
        border.MouseLeftButtonDown += (s, e) => FocusSlot(index);
        return border;
    }

    protected void OnPropertyChanged([CallerMemberName] string n = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
