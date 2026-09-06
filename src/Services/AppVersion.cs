using System.Reflection;

namespace Tfx;

/// <summary>
/// The product version shown in the window title, the status bar, and by
/// <c>tfx --version</c>. Read from the assembly's informational version (set
/// from <c>&lt;Version&gt;</c> in Tfx.csproj), with any "+commit" suffix dropped.
/// </summary>
internal static class AppVersion
{
    public static string Value { get; } = Load();

    private static string Load()
    {
        var attr = typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var version = attr?.InformationalVersion ?? "";
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
