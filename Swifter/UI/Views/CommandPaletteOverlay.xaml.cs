using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Swifter.Core.Productivity;
using Swifter.UI.ViewModels;

namespace Swifter.UI.Views;

public partial class CommandPaletteOverlay : UserControl
{
    public CommandPaletteEngine Engine { get; private set; }
    public event Action<PaletteResult> ResultChosen;

    public Brush TextPrimaryBrush => App.ThemeManager?.IsDark == true ? new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)) : new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
    public Brush TextSecondaryBrush => App.ThemeManager?.IsDark == true ? new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)) : new SolidColorBrush(Color.FromRgb(0x5C, 0x5C, 0x5C));

    public CommandPaletteOverlay()
    {
        InitializeComponent();
        DataContext = this;
        IsVisibleChanged += (s, e) =>
        {
            if (IsVisible)
            {
                QueryBox.Text = "";
                QueryBox.Focus();
            }
        };
    }

    public void Attach(CommandPaletteEngine engine)
    {
        Engine = engine;
        ResultsList.ItemsSource = engine.Results;
        engine.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(CommandPaletteEngine.Results) || e.PropertyName == nameof(CommandPaletteEngine.Query))
                Dispatcher.Invoke(UpdateCount);
        };
        UpdateCount();
    }

    public void ActivatePalette()
    {
        Visibility = Visibility.Visible;
        QueryBox.Text = Engine?.Query ?? "";
        QueryBox.Focus();
        QueryBox.CaretIndex = QueryBox.Text.Length;
        UpdateCount();
    }

    public void Dismiss()
    {
        Visibility = Visibility.Collapsed;
    }

    private void UpdateCount()
    {
        if (ResultCount != null && Engine != null)
            ResultCount.Text = Engine.Results.Count + " results";
        if (CategoryHint != null && Engine != null)
            CategoryHint.Text = string.IsNullOrEmpty(Engine.Query) ? "SUGGESTED" : "MATCHES";
    }

    private void OnQueryKeyDown(object sender, KeyEventArgs e)
    {
        if (Engine == null) return;
        switch (e.Key)
        {
            case Key.Enter:
                Engine.ActivateSelected();
                e.Handled = true;
                break;
            case Key.Down:
                Engine.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                Engine.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Escape:
                Engine.Close();
                Visibility = Visibility.Collapsed;
                e.Handled = true;
                break;
        }
    }

    private void OnQueryChanged(object sender, TextChangedEventArgs e)
    {
        if (Engine == null) return;
        Engine.Query = QueryBox.Text;
        UpdateCount();
    }

    private void OnResultClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.DataContext is PaletteResult result)
        {
            if (Engine != null)
            {
                var index = Engine.Results.IndexOf(result);
                if (index >= 0) Engine.SelectedIndex = index;
            }
            Choose(result);
        }
    }

    private void Choose(PaletteResult result)
    {
        ResultChosen?.Invoke(result);
        if (Engine != null)
        {
            var index = Engine.Results.IndexOf(result);
            if (index >= 0) Engine.SelectedIndex = index;
            Engine.ActivateSelected();
        }
        Visibility = Visibility.Collapsed;
    }

    private void OnBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == sender)
        {
            Engine?.Close();
            Visibility = Visibility.Collapsed;
        }
    }
}
