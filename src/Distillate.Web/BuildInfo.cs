using System.Reflection;

namespace Distillate.Web;

/// <summary>
/// The identity of the running build, read from the assembly rather than written
/// down a second time. <see cref="Version"/> comes from <c>Directory.Build.props</c>;
/// <see cref="Commit"/> is appended by the container build as
/// <c>--build-arg DISTILLATE_BUILD=&lt;sha&gt;</c>, so a bug report can name the
/// commit that produced the binary instead of a shell prompt on the build machine.
/// </summary>
internal static class BuildInfo
{
    /// <summary>Semantic version, e.g. <c>0.1.0</c>.</summary>
    public static string Version { get; } = ReadParts().Version;

    /// <summary>Source revision the image was built from, or <c>unknown</c>.</summary>
    public static string Commit { get; } = ReadParts().Commit;

    /// <summary><c>0.1.0</c> when built locally, <c>0.1.0+1a2b3c4</c> from CI.</summary>
    public static string Full => Commit == "unknown" ? Version : $"{Version}+{Commit}";

    private static (string Version, string Commit) ReadParts()
    {
        var informational = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
            return ("0.0.0", "unknown");

        // InformationalVersion is "<version>+<commit>", optionally followed by
        // SourceLink metadata after a second '+'. Only the first two parts are ours.
        var plus = informational.IndexOf('+');
        if (plus < 0)
            return (informational, "unknown");

        var version = informational[..plus];
        var commit = informational[(plus + 1)..];
        var secondPlus = commit.IndexOf('+');
        if (secondPlus >= 0)
            commit = commit[..secondPlus];

        return (version, string.IsNullOrWhiteSpace(commit) ? "unknown" : commit);
    }
}
