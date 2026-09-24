using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Swifter.Core.Downloads;

namespace Swifter.UI.ViewModels;

public sealed class DownloadRowViewModel : ObservableObject
{
    private DownloadItem _item;

    public DownloadItem Item
    {
        get => _item;
        set { _item = value; OnPropertyChanged(); OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(PercentText)); OnPropertyChanged(nameof(StateText)); OnPropertyChanged(nameof(SpeedText)); OnPropertyChanged(nameof(EtaText)); OnPropertyChanged(nameof(SizeText)); }
    }

    public Guid Id => _item?.Id ?? Guid.Empty;
    public string Name => _item?.FileName ?? "…";
    public string Url => _item?.Url;
    public double Percent => _item?.Percent ?? 0;
    public string PercentText => Percent.ToString("0.0") + "%";
    public string StateText => _item?.State.ToString() ?? "";
    public string SpeedText => _item != null && _item.SpeedBytesPerSec > 0 ? FormatBytes(_item.SpeedBytesPerSec) + "/s" : "—";
    public string EtaText
    {
        get
        {
            var eta = _item?.EstimatedRemaining;
            if (!eta.HasValue) return "—";
            var t = eta.Value;
            if (t.TotalHours >= 1) return (int)t.TotalHours + "h " + t.Minutes + "m";
            if (t.TotalMinutes >= 1) return t.Minutes + "m " + t.Seconds + "s";
            return Math.Ceiling(t.TotalSeconds) + "s";
        }
    }
    public string SizeText => _item == null ? "—" : FormatBytes(_item.ReceivedBytes) + " / " + (_item.TotalBytes > 0 ? FormatBytes(_item.TotalBytes) : "?");
    public string SegmentSummary
    {
        get
        {
            if (_item?.Segments == null || _item.Segments.Count == 0) return "";
            var done = _item.Segments.Count(s => s.Completed);
            return $"{done}/{_item.Segments.Count} segs";
        }
    }
    public bool IsActive => _item != null && _item.State < DownloadState.Completed;
    public bool CanPause => _item != null && _item.State == DownloadState.Downloading;
    public bool CanResume => _item != null && (_item.State == DownloadState.Paused || _item.State == DownloadState.Failed || _item.State == DownloadState.Queued);

    public static string FormatBytes(double bytes)
    {
        if (bytes < 1024) return bytes.ToString("0") + " B";
        string[] units = { "KB", "MB", "GB", "TB" };
        var v = bytes;
        var i = -1;
        do { v /= 1024; i++; } while (v >= 1024 && i < units.Length - 1);
        return v.ToString(v >= 10 ? "0" : "0.0") + " " + units[i];
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Item));
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(PercentText));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(SpeedText));
        OnPropertyChanged(nameof(EtaText));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(SegmentSummary));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
    }
}

public sealed class DownloadsViewModel : ObservableObject, IDisposable
{
    private readonly SegmentedDownloadEngine _engine;
    private DownloadRowViewModel _selected;
    private string _filter = "";
    private long _globalThrottle;
    private int _segmentCount = 16;

    public ObservableCollection<DownloadRowViewModel> Items { get; } = new ObservableCollection<DownloadRowViewModel>();
    public DownloadRowViewModel Selected { get => _selected; set => Set(ref _selected, value); }
    public string Filter { get => _filter; set { Set(ref _filter, value); ApplyFilter(); } }
    public long GlobalThrottle { get => _globalThrottle; set => Set(ref _globalThrottle, value); }
    public int SegmentCount { get => _segmentCount; set => Set(ref _segmentCount, value); }
    public int ActiveCount => Items.Count(i => i.IsActive);
    public string AggregateSpeed
    {
        get
        {
            var total = Items.Sum(i => i.Item?.SpeedBytesPerSec ?? 0);
            return DownloadRowViewModel.FormatBytes(total) + "/s";
        }
    }

    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand PauseAllCommand { get; }
    public ICommand ResumeAllCommand { get; }
    public ICommand ClearCompletedCommand { get; }
    public ICommand OpenFolderCommand { get; }

    public event Action<string> StatusRaised;

    public DownloadsViewModel(SegmentedDownloadEngine engine)
    {
        _engine = engine;
        _engine.ItemUpdated += OnItemUpdated;
        _engine.ItemCompleted += i => StatusRaised?.Invoke(i.FileName + " completed");
        _engine.ItemFailed += i => StatusRaised?.Invoke(i.FileName + " failed: " + i.Error);

        PauseCommand = new RelayCommand(o => { if (o is Guid id) _engine.Pause(id); }, o => o is Guid);
        ResumeCommand = new RelayCommand(o => { if (o is Guid id) _engine.Resume(id); }, o => o is Guid);
        CancelCommand = new RelayCommand(o => { if (o is Guid id) _engine.Cancel(id); }, o => o is Guid);
        PauseAllCommand = new RelayCommand(_ => { foreach (var row in Items.Where(i => i.CanPause)) _engine.Pause(row.Id); });
        ResumeAllCommand = new RelayCommand(_ => { foreach (var row in Items.Where(i => i.CanResume)) _engine.Resume(row.Id); });
        ClearCompletedCommand = new RelayCommand(_ =>
        {
            var done = Items.Where(i => !i.IsActive).ToList();
            foreach (var d in done) Items.Remove(d);
        });
        OpenFolderCommand = new RelayCommand(_ =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _engine.DownloadDirectory,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        });
    }

    private void OnItemUpdated(DownloadItem item)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var row = Items.FirstOrDefault(i => i.Id == item.Id);
            if (row == null)
            {
                row = new DownloadRowViewModel { Item = item };
                Items.Insert(0, row);
            }
            else
            {
                row.Item = item;
                row.Refresh();
            }
            OnPropertyChanged(nameof(ActiveCount));
            OnPropertyChanged(nameof(AggregateSpeed));
        });
    }

    private void ApplyFilter()
    {
    }

    public async Task<DownloadRowViewModel> EnqueueAsync(string url, string referer = null, string name = null, bool media = false, string mediaType = null)
    {
        var item = await _engine.EnqueueAsync(url, referer, name, media, mediaType).ConfigureAwait(true);
        return Items.FirstOrDefault(i => i.Id == item.Id);
    }

    public void SetThrottle(Guid id, long bytesPerSec)
    {
        _engine.SetThrottle(id, bytesPerSec);
    }

    public string BuildStatusLine()
    {
        if (ActiveCount == 0) return "No active downloads";
        return $"{ActiveCount} active · {AggregateSpeed} · {Items.Count} total";
    }

    public IReadOnlyList<DownloadItem> Snapshot() => _engine.GetAll();

    public void Dispose()
    {
        _engine.ItemUpdated -= OnItemUpdated;
    }
}
