using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Swifter.Core.Extensions;

public sealed class ExtensionMessage
{
    public string ExtensionId { get; set; }
    public string Type { get; set; }
    public JsonElement Payload { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

public sealed class BackgroundScriptRunner
{
    private readonly ConcurrentDictionary<string, ExtensionRuntime> _runtimes = new ConcurrentDictionary<string, ExtensionRuntime>();
    private readonly object _lock = new object();
    public event Action<ExtensionMessage> MessageEmitted;
    public event Action<string, string> LogLine;

    private sealed class ExtensionRuntime
    {
        public string ExtensionId { get; set; }
        public string WorkerPath { get; set; }
        public Process Process { get; set; }
        public StreamWriter Stdin { get; set; }
        public bool Alive => Process != null && !Process.HasExited;
        public StringBuilder Log { get; } = new StringBuilder();
        public DateTime StartedUtc { get; set; }
    }

    public bool StartServiceWorker(string extensionId, string workerPath)
    {
        if (string.IsNullOrEmpty(extensionId) || string.IsNullOrEmpty(workerPath) || !File.Exists(workerPath)) return false;
        lock (_lock)
        {
            StopServiceWorker(extensionId);
            try
            {
                var runtime = new ExtensionRuntime
                {
                    ExtensionId = extensionId,
                    WorkerPath = workerPath,
                    StartedUtc = DateTime.UtcNow
                };
                var psi = new ProcessStartInfo
                {
                    FileName = "node",
                    ArgumentList = { workerPath },
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = Path.GetDirectoryName(workerPath) ?? ""
                };
                psi.Environment["SWIFTER_EXTENSION_ID"] = extensionId;
                psi.Environment["SWIFTER_EXTENSION_MODE"] = "mv3-service-worker";
                var proc = Process.Start(psi);
                if (proc == null) return false;
                runtime.Process = proc;
                runtime.Stdin = proc.StandardInput;
                proc.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    runtime.Log.AppendLine(e.Data);
                    LogLine?.Invoke(extensionId, e.Data);
                };
                proc.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    runtime.Log.AppendLine("[err] " + e.Data);
                    LogLine?.Invoke(extensionId, e.Data);
                };
                proc.Exited += (s, e) =>
                {
                    _runtimes.TryRemove(extensionId, out _);
                    LogLine?.Invoke(extensionId, "service worker exited");
                };
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                _runtimes[extensionId] = runtime;
                return true;
            }
            catch (Exception ex)
            {
                LogLine?.Invoke(extensionId, "start failed: " + ex.Message);
                return false;
            }
        }
    }

    public void StopServiceWorker(string extensionId)
    {
        if (!_runtimes.TryRemove(extensionId, out var runtime)) return;
        try
        {
            if (runtime.Stdin != null)
            {
                runtime.Stdin.WriteLine("__swifter_shutdown__");
                runtime.Stdin.Flush();
            }
        }
        catch
        {
        }
        try
        {
            if (runtime.Process != null && !runtime.Process.HasExited)
            {
                if (!runtime.Process.WaitForExit(1500))
                    runtime.Process.Kill(true);
            }
        }
        catch
        {
        }
        finally
        {
            runtime.Process?.Dispose();
            runtime.Stdin?.Dispose();
        }
    }

    public void StopAll()
    {
        foreach (var id in _runtimes.Keys.ToList()) StopServiceWorker(id);
    }

    public bool PostMessage(string extensionId, string json)
    {
        if (!_runtimes.TryGetValue(extensionId, out var runtime) || !runtime.Alive) return false;
        try
        {
            runtime.Stdin.WriteLine(json);
            runtime.Stdin.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool SendRuntimeMessage(ExtensionMessage message)
    {
        try
        {
            var json = JsonSerializer.Serialize(new
            {
                channel = "runtime",
                type = message.Type,
                payload = message.Payload,
                time = message.TimestampUtc
            });
            var sent = PostMessage(message.ExtensionId, json);
            if (sent) MessageEmitted?.Invoke(message);
            return sent;
        }
        catch
        {
            return false;
        }
    }

    public string GetLog(string extensionId, int maxLines = 200)
    {
        if (!_runtimes.TryGetValue(extensionId, out var runtime)) return "";
        var lines = runtime.Log.ToString().Split('\n');
        return string.Join("\n", lines.TakeLast(maxLines));
    }

    public bool IsRunning(string extensionId)
    {
        return _runtimes.TryGetValue(extensionId, out var r) && r.Alive;
    }

    public IReadOnlyList<string> RunningExtensions()
    {
        return _runtimes.Values.Where(r => r.Alive).Select(r => r.ExtensionId).ToList();
    }

    public Dictionary<string, object> StatusSnapshot()
    {
        return _runtimes.ToDictionary(kv => kv.Key, kv => (object)new
        {
            alive = kv.Value.Alive,
            pid = kv.Value.Process?.Id ?? 0,
            started = kv.Value.StartedUtc,
            workingSet = SafeWorkingSet(kv.Value.Process)
        });
    }

    private static long SafeWorkingSet(Process p)
    {
        try
        {
            if (p == null || p.HasExited) return 0;
            p.Refresh();
            return p.WorkingSet64;
        }
        catch
        {
            return 0;
        }
    }

    public string BuildPageInjection(ExtensionManifest manifest, List<string> scripts, string world)
    {
        if (scripts.Count == 0) return "";
        var sb = new StringBuilder();
        sb.AppendLine("(function(){");
        sb.AppendLine($"try{{window.__swifter_ext_{Sanitize(manifest.Name)}__=true;}}catch(e){{}}");
        foreach (var script in scripts)
        {
            sb.AppendLine($"try{{/* {Path.GetFileName(script)} */}}catch(e){{}}");
        }
        sb.AppendLine("})();");
        return sb.ToString();
    }

    public string LoadScriptSource(string path, string extensionRoot)
    {
        try
        {
            var full = Path.GetFullPath(Path.Combine(extensionRoot, path));
            var rootFull = Path.GetFullPath(extensionRoot);
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return "";
            if (!File.Exists(full)) return "";
            var source = File.ReadAllText(full);
            source = RegexReplace(source, @"chrome\.runtime\.sendMessage\(", "window.__swifter_rt_send__(");
            source = RegexReplace(source, @"chrome\.storage\.local", "window.__swifter_storage_local");
            source = RegexReplace(source, @"chrome\.tabs", "window.__swifter_tabs");
            source = RegexReplace(source, @"chrome\.scripting", "window.__swifter_scripting");
            return source;
        }
        catch
        {
            return "";
        }
    }

    private static string RegexReplace(string input, string pattern, string replacement)
    {
        return System.Text.RegularExpressions.Regex.Replace(input, pattern, replacement);
    }

    private static string Sanitize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "ext";
        return new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    }
}
