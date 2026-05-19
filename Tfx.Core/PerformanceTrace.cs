using System.Diagnostics;

namespace Tfx;

/// <summary>
/// Lightweight timing trace. Enabled when either the
/// <c>TFX_PERFORMANCE_LOGS=1</c> environment variable is set at startup, or the
/// host calls <see cref="SetEnabled(bool)"/> based on a persisted setting.
/// </summary>
/// <remarks>
/// When disabled, all <see cref="Measure(string, Action)"/> /
/// <see cref="Measure{T}(string, Func{T})"/> / <see cref="Begin(string)"/>
/// calls fall through to the wrapped delegate with zero overhead beyond the
/// guard, so trace points can be sprinkled freely in hot paths.
/// </remarks>
public static class PerformanceTrace
{
    public const string EnvironmentVariable = "TFX_PERFORMANCE_LOGS";

    private static readonly bool EnvEnabled =
        string.Equals(Environment.GetEnvironmentVariable(EnvironmentVariable), "1", StringComparison.Ordinal);

    private static volatile bool _settingEnabled;

    public static bool IsEnabled => EnvEnabled || _settingEnabled;

    /// <summary>
    /// Toggles the trace from the host (e.g. driven by a UserDefaults-style
    /// setting). The environment variable, when set, always keeps the trace
    /// active regardless of this flag.
    /// </summary>
    public static void SetEnabled(bool enabled) => _settingEnabled = enabled;

    public static IDisposable? Begin(string label) =>
        IsEnabled ? new Span(label) : null;

    public static void Measure(string label, Action action)
    {
        if (!IsEnabled)
        {
            action();
            return;
        }
        var sw = Stopwatch.StartNew();
        try
        {
            action();
        }
        finally
        {
            sw.Stop();
            Log(label, sw.Elapsed);
        }
    }

    public static T Measure<T>(string label, Func<T> action)
    {
        if (!IsEnabled)
        {
            return action();
        }
        var sw = Stopwatch.StartNew();
        try
        {
            return action();
        }
        finally
        {
            sw.Stop();
            Log(label, sw.Elapsed);
        }
    }

    private static void Log(string label, TimeSpan elapsed)
    {
        var line = $"[tfx perf] {label,-44} {elapsed.TotalMilliseconds,9:0.000} ms";
        Debug.WriteLine(line);
        Console.WriteLine(line);
    }

    private sealed class Span : IDisposable
    {
        private readonly string _label;
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private int _disposed;

        public Span(string label) => _label = label;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }
            _sw.Stop();
            Log(_label, _sw.Elapsed);
        }
    }
}
