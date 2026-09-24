using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Swifter.Core.Storage;

namespace Swifter;

public static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessDPIAware();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr min, IntPtr max);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreateMutex(bool initialOwner, string name, out IntPtr mutex);

    private static Mutex _singleInstanceMutex;

    [STAThread]
    public static void Main(string[] args)
    {
        CreateMutex(true, "SwifterBrowserSingleInstanceMutex", out _singleInstanceMutex);
        SetProcessDPIAware();
        EnsureUserDirectories();
        ApplyCriticalRuntimeFlags();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    private static void ApplyCriticalRuntimeFlags()
    {
        try
        {
            var proc = Process.GetCurrentProcess();
            SetProcessWorkingSetSize(proc.Handle, (IntPtr)(-1), (IntPtr)(-1));
        }
        catch
        {
        }
    }

    private static void EnsureUserDirectories()
    {
        var root = GetUserDataRoot();
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "profiles"));
        Directory.CreateDirectory(Path.Combine(root, "extensions"));
        Directory.CreateDirectory(Path.Combine(root, "cache"));
        Directory.CreateDirectory(Path.Combine(root, "downloads"));
        Directory.CreateDirectory(Path.Combine(root, "wallpapers"));
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        Directory.CreateDirectory(Path.Combine(root, "temp"));
    }

    public static string GetUserDataRoot()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "Swifter");
    }

    public static string GetDatabasePath()
    {
        return Path.Combine(GetUserDataRoot(), "swifter.db");
    }

    public static string GetVersionString()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return info?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "1.0.0";
    }

    public static string ComputeMachineKey()
    {
        var machine = Environment.MachineName + Environment.UserName + Environment.ProcessorCount;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("Swifter.v1." + machine));
        return Convert.ToBase64String(bytes);
    }
}
