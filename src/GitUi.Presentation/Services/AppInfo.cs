using System.Reflection;
using System.Runtime.InteropServices;

namespace GitUi.Presentation.Services;

/// <summary>What the About window and the title bar say about the running build.</summary>
public static class AppInfo
{
    public const string Name = "Git UI";
    public const string Author = "Dimitri Even";
    public const string Copyright = "© 2026 Dimitri Even";
    public const string RepositoryUrl = "https://github.com/Deve1ne/git-ui";
    public const string ReleasesUrl = "https://github.com/Deve1ne/git-ui/releases";
    public const string LicenseName = "GNU General Public License v3.0 or later";
    public const string LicenseUrl = "https://www.gnu.org/licenses/gpl-3.0.html";

    /// <summary>The product version without the commit hash, e.g. "0.2.1" (null when not stamped).</summary>
    public static string? Version { get; } = InformationalVersion()?.Split('+')[0];

    /// <summary>The commit the build comes from, shortened, e.g. "03aace5" (null when not stamped).</summary>
    public static string? Commit { get; } = InformationalVersion()?.Split('+').Skip(1).FirstOrDefault() is { Length: > 0 } sha
        ? sha[..Math.Min(7, sha.Length)]
        : null;

    /// <summary>e.g. ".NET 8.0.11 · Windows x64".</summary>
    public static string Runtime =>
        $"{RuntimeInformation.FrameworkDescription} · {OperatingSystemName()} {RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}";

    private static string? InformationalVersion() =>
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    private static string OperatingSystemName() =>
        OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsLinux() ? "Linux" : "Unknown OS";
}
