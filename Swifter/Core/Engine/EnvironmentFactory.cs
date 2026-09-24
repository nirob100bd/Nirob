using Microsoft.Web.WebView2.Core;

namespace Swifter.Core.Engine;

public sealed class EnvironmentFactory
{
    private CoreWebView2Environment _environment;
    private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);
    private bool _initialized;

    public string UserDataFolder { get; private set; }
    public bool IsLowEndProfile { get; private set; }
    public CoreWebView2Environment Environment => _environment;

    public EnvironmentFactory()
    {
        UserDataFolder = Path.Combine(Program.GetUserDataRoot(), "profiles", "default");
        IsLowEndProfile = NativeMemoryBridge.IsLowEndCpu();
    }

    public async Task InitializeAsync()
    {
        if (_initialized && _environment != null) return;
        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized && _environment != null) return;
            Directory.CreateDirectory(UserDataFolder);
            var options = new CoreWebView2EnvironmentOptions();
            options.AdditionalBrowserArguments = BuildBrowserArguments();
            options.Language = "en-US";
            options.TargetCompatibleBrowserVersion = "120.0.0.0";
            options.AllowSingleFileExecution = true;
            _environment = await CoreWebView2Environment.CreateAsync(null, UserDataFolder, options).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private string BuildBrowserArguments()
    {
        var args = new List<string>
        {
            "--disable-features=Translate,MediaRouter",
            "--enable-parallel-downloading",
            "--disable-background-networking",
            "--metrics-recording-only",
            "--disable-domain-reliability",
            "--no-first-run",
            "--disable-search-engine-choice-screen",
            "--disable-component-update",
            "--autoplay-policy=no-user-gesture-required"
        };
        if (IsLowEndProfile)
        {
            args.Add("--renderer-process-limit=4");
            args.Add("--disable-gpu-vsync");
            args.Add("--num-raster-threads=1");
            args.Add("--disable-gpu-memory-buffer-video-frames");
            args.Add("--js-flags=--max-old-space-size=256");
        }
        else
        {
            args.Add("--enable-gpu-rasterization");
            args.Add("--enable-zero-copy");
            args.Add("--num-raster-threads=4");
        }
        return string.Join(" ", args);
    }

    public async Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        if (_environment == null) await InitializeAsync().ConfigureAwait(false);
        return _environment;
    }

    public async Task<bool> EnsureControllerAsync(Microsoft.Web.WebView2.Wpf.CoreWebView2 host)
    {
        try
        {
            var env = await GetEnvironmentAsync().ConfigureAwait(false);
            await host.EnsureCoreWebView2Async(env).ConfigureAwait(true);
            return host.CoreWebView2 != null;
        }
        catch
        {
            return false;
        }
    }

    public CoreWebView2Profile CreateIsolatedProfile(string profileId)
    {
        if (_environment == null) throw new InvalidOperationException("Environment not initialized");
        var safe = Sanitize(profileId);
        return _environment.CreateCoreWebView2Profile(safe);
    }

    private static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input)) return "default";
        var chars = input.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray();
        return new string(chars);
    }
}
