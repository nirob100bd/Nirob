using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Swifter.Core.Engine;
using Swifter.Core.Protocols;
using Swifter.Core.Shield;
using Swifter.Core.Storage;
using Swifter.Theme;
using Swifter.UI.Views;

namespace Swifter;

public partial class App : Application
{
    public static DatabaseContext Database { get; private set; }
    public static AdBlockEngine AdBlock { get; private set; }
    public static MemoryTrimmingService MemoryTrimmer { get; private set; }
    public static EnvironmentFactory WebViewEnvironment { get; private set; }
    public static TrackingParamStripper ParamStripper { get; private set; }
    public static FingerprintProtector FingerprintProtector { get; private set; }
    public static WebRtcProtectionService WebRtcProtection { get; private set; }
    public static SpeedThrottleRegistry SpeedThrottle { get; private set; }
    public static ThemeManager ThemeManager { get; private set; }
    public static AccentColorProvider AccentProvider { get; private set; }
    public static CustomSchemeHandler SchemeHandler { get; private set; }
    public static ShortcutManager Hotkeys { get; private set; }
    public static HibernationService Hibernator { get; private set; }
    public static ContainerProfileManager Containers { get; private set; }
    public static CacheManager Cache { get; private set; }
    public static SessionRepository Sessions { get; private set; }
    public static HistoryRepository History { get; private set; }
    public static BookmarkRepository Bookmarks { get; private set; }
    public static PasswordAutofillManager Autofill { get; private set; }
    public static ExtensionHostEngine ExtensionHost { get; private set; }
    public static HlsStreamSniffer MediaSniffer { get; private set; }
    public static MediaOverlayInjector MediaOverlay { get; private set; }
    public static List<string> StartupArgs { get; private set; } = new List<string>();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        StartupArgs = e.Args?.ToList() ?? new List<string>();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        InitializeServices();
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private void InitializeServices()
    {
        Database = new DatabaseContext(Program.GetDatabasePath());
        Database.Initialize();
        History = new HistoryRepository(Database);
        Bookmarks = new BookmarkRepository(Database);
        Sessions = new SessionRepository(Database);
        Autofill = new PasswordAutofillManager(Database, Program.ComputeMachineKey());
        Cache = new CacheManager(Database);
        AccentProvider = new AccentColorProvider();
        ThemeManager = new ThemeManager(AccentProvider);
        ThemeManager.LoadTheme();
        AdBlock = new AdBlockEngine();
        AdBlock.LoadRulesAsync().ConfigureAwait(false);
        MemoryTrimmer = new MemoryTrimmingService();
        MemoryTrimmer.Start();
        WebViewEnvironment = new EnvironmentFactory();
        WebViewEnvironment.InitializeAsync().ConfigureAwait(false);
        ParamStripper = new TrackingParamStripper();
        FingerprintProtector = new FingerprintProtector();
        WebRtcProtection = new WebRtcProtectionService();
        WebRtcProtection.Start();
        SpeedThrottle = new SpeedThrottleRegistry();
        SchemeHandler = new CustomSchemeHandler();
        Hotkeys = new ShortcutManager();
        Hotkeys.RegisterDefaults();
        Hibernator = new HibernationService(Sessions);
        Hibernator.Start();
        Containers = new ContainerProfileManager();
        ExtensionHost = new ExtensionHostEngine();
        ExtensionHost.LoadInstalledExtensionsAsync().ConfigureAwait(false);
        MediaSniffer = new HlsStreamSniffer();
        MediaOverlay = new MediaOverlayInjector();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) LogCrash(ex);
    }

    private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        e.SetObserved();
    }

    private void LogCrash(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Program.GetUserDataRoot(), "logs");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "crash-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            File.WriteAllText(file, DateTime.Now.ToString("o") + Environment.NewLine + ex.ToString());
        }
        catch
        {
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Hibernator?.Stop();
            MemoryTrimmer?.Stop();
            WebRtcProtection?.Stop();
            Sessions?.SaveCurrentSessionAsync().ConfigureAwait(false);
            Database?.Dispose();
        }
        catch
        {
        }
        base.OnExit(e);
    }
}
