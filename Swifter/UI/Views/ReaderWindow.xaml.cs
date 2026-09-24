using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Swifter.Core.Productivity;

namespace Swifter.UI.Views;

public partial class ReaderWindow : Window
{
    private ArticleContent _article;
    private ReaderTypography _typo = new ReaderTypography();
    private bool _webReady;

    public ReaderWindow(ArticleContent article)
    {
        _article = article ?? new ArticleContent();
        InitializeComponent();
        Title = (_article.Title ?? "Reader") + " — Swifter Reader";
        TitleBlock.Text = _article.Title ?? "Untitled";
        var metaParts = new List<string>();
        if (!string.IsNullOrEmpty(_article.SiteName)) metaParts.Add(_article.SiteName);
        if (!string.IsNullOrEmpty(_article.Byline)) metaParts.Add(_article.Byline);
        if (_article.WordCount > 0) metaParts.Add(_article.WordCount + " words · " + Math.Max(1, _article.WordCount / 220) + " min read");
        if (!string.IsNullOrEmpty(_article.Url)) metaParts.Add(_article.Url);
        MetaBlock.Text = string.Join("  ·  ", metaParts);
        ExcerptBlock.Text = _article.Excerpt ?? "";
        WordCountBlock.Text = _article.WordCount + " words extracted · score " + _article.Score.ToString("0") + " · " + (_article.ExtractedUtc.ToLocalTime().ToString("g"));
        Loaded += async (s, e) => await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            await App.WebViewEnvironment.EnsureControllerAsync(ArticleView).ConfigureAwait(true);
            ArticleView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            ArticleView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            ArticleView.CoreWebView2.Settings.IsBuiltInErrorPageEnabled = false;
            ArticleView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            ArticleView.CoreWebView2.Settings.IsSwipeNavigationEnabled = false;
            _webReady = true;
            RenderArticle();
        }
        catch (Exception ex)
        {
            WordCountBlock.Text = "Reader engine error: " + ex.Message;
        }
    }

    private void RenderArticle()
    {
        if (!_webReady || ArticleView.CoreWebView2 == null) return;
        _typo.Theme = ThemeBox.SelectedIndex switch
        {
            1 => ReaderTheme.Sepia,
            2 => ReaderTheme.Dark,
            3 => ReaderTheme.Black,
            _ => ReaderTheme.Light
        };
        var extractor = new ArticleExtractor();
        var html = extractor.BuildReaderHtml(_article, _typo.Theme, _typo);
        ArticleView.CoreWebView2.NavigateToString(html);
        ApplyChromeTheme(_typo.Theme);
    }

    private void ApplyChromeTheme(ReaderTheme theme)
    {
        var (bg, toolbar, fg) = theme switch
        {
            ReaderTheme.Dark => ((Color)ColorConverter.ConvertFromString("#141414"), (Color)ColorConverter.ConvertFromString("#1C1C22"), (Color)ColorConverter.ConvertFromString("#EEEEEE")),
            ReaderTheme.Black => ((Color)ColorConverter.ConvertFromString("#000000"), (Color)ColorConverter.ConvertFromString("#0A0A0A"), (Color)ColorConverter.ConvertFromString("#D9D9D9")),
            ReaderTheme.Sepia => ((Color)ColorConverter.ConvertFromString("#F4ECD8"), (Color)ColorConverter.ConvertFromString("#EBE0C4"), (Color)ColorConverter.ConvertFromString("#3B2F1E")),
            _ => ((Color)ColorConverter.ConvertFromString("#FAFAF8"), (Color)ColorConverter.ConvertFromString("#F0EDE6"), (Color)ColorConverter.ConvertFromString("#1B1B1F"))
        };
        Background = new SolidColorBrush(bg);
        PageBorder.Background = new SolidColorBrush(bg);
        Toolbar.Background = new SolidColorBrush(toolbar);
        TitleBlock.Foreground = new SolidColorBrush(fg);
        Theme.NativeGlassEngine.RefreshTheme(this, theme is ReaderTheme.Dark or ReaderTheme.Black);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeBox.SelectedIndex = 0;
    }

    private void OnFontChanged(object sender, SelectionChangedEventArgs e)
    {
        _typo.Font = FontBox.SelectedIndex switch
        {
            1 => ReaderFont.Serif,
            2 => ReaderFont.Mono,
            _ => ReaderFont.Sans
        };
        RenderArticle();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        RenderArticle();
    }

    private void OnWidthChanged(object sender, SelectionChangedEventArgs e)
    {
        _typo.ContentWidth = WidthBox.SelectedIndex switch
        {
            0 => 580,
            2 => 960,
            _ => 720
        };
        RenderArticle();
    }

    private void OnFontLarger(object sender, RoutedEventArgs e)
    {
        _typo.FontSize = Math.Min(28, _typo.FontSize + 1);
        FontSizeLabel.Text = _typo.FontSize.ToString();
        RenderArticle();
    }

    private void OnFontSmaller(object sender, RoutedEventArgs e)
    {
        _typo.FontSize = Math.Max(14, _typo.FontSize - 1);
        FontSizeLabel.Text = _typo.FontSize.ToString();
        RenderArticle();
    }

    private async void OnSpeak(object sender, RoutedEventArgs e)
    {
        if (!_webReady) return;
        try
        {
            var result = await ArticleView.CoreWebView2.ExecuteScriptAsync("swifterSpeak&&swifterSpeak(),'ok'");
            PlayBtn.Content = "🔊 Speaking…";
        }
        catch
        {
            try
            {
                var text = _article.TextContent ?? "";
                if (!string.IsNullOrWhiteSpace(text))
                {
                    System.Windows.Speech.Synthesis.SpeechSynthesizer synth = null;
                    try
                    {
                        synth = new System.Windows.Speech.Synthesis.SpeechSynthesizer();
                        synth.Speak(text.Substring(0, Math.Min(text.Length, 4000)));
                    }
                    finally
                    {
                        synth?.Dispose();
                    }
                }
            }
            catch
            {
            }
        }
    }

    private async void OnPauseSpeak(object sender, RoutedEventArgs e)
    {
        if (!_webReady) return;
        try { await ArticleView.CoreWebView2.ExecuteScriptAsync("swifterPauseTts&&swifterPauseTts(),'ok'"); } catch { }
    }

    private async void OnStopSpeak(object sender, RoutedEventArgs e)
    {
        if (!_webReady) return;
        try { await ArticleView.CoreWebView2.ExecuteScriptAsync("swifterStopTts&&swifterStopTts(),'ok'"); } catch { }
        PlayBtn.Content = "🔊 Read aloud";
    }

    private void OnCopyText(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = _article.TextContent ?? "";
            Clipboard.SetText(text);
        }
        catch
        {
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
