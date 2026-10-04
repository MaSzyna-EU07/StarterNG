using System.Threading;
using System.Threading.Tasks;
using StarterNG.Domain.Settings;

namespace StarterNG.Application.Abstractions;

/// <summary>
/// A published build of the starter: its tag, the page it is downloaded from and
/// the commit it was built from - the only way to tell rolling builds apart.
/// </summary>
public sealed record ReleaseInfo(string Tag, string PageUrl, string? Commit = null);

/// <summary>
/// What asking for the newest release came back with. No answer - offline, rate
/// limited, timed out - is kept apart from an answer of "nothing released", so a
/// failed check is not remembered as a done one.
/// </summary>
public sealed record ReleaseLookup(bool Answered, ReleaseInfo? Release)
{
    public static ReleaseLookup NoAnswer { get; } = new(false, null);

    public static ReleaseLookup Found(ReleaseInfo? release) => new(true, release);
}

public interface IReleaseFeed
{
    Task<ReleaseLookup> LatestAsync(UpdateChannel channel, CancellationToken cancellationToken = default);
}
