using System.Net;
using System.Net.Http;
using StarterNG.Application;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Settings;
using StarterNG.Infrastructure.Adapters;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class UpdateCheckTests
{
    private const string Page = "https://github.com/MaSzyna-EU07/StarterNG/releases/tag/v1.4.107";
    private const string StagingPage = "https://github.com/MaSzyna-EU07/StarterNG/releases/tag/staging";
    private const string RecordPath = "/home/user/.config/starter/updatecheck.txt";
    private const string RunningCommit = "9d981cb0932c372074eaba25d5c917acbacc25d8";
    private const string OtherCommit = "f7219cf7055670ad0f39fc18e0640358b8fff8f7";

    private static readonly Version Current = new(1, 4, 100, 0);

    [Theory]
    [InlineData("v1.4.107")]
    [InlineData("v1.5.0")]
    [InlineData("1.4.101")]
    public async Task A_higher_release_is_an_update(string tag)
    {
        var check = new Rig(new StubFeed(Stable(tag))).Check;

        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, await check.CheckNowAsync(UpdateChannel.Stable));
        Assert.Equal(UpdateCheck.ParseTag(tag)!.ToString(), check.Available!.Label);
        Assert.Equal(Page, check.Available.PageUrl);
    }

    [Theory]
    [InlineData("v1.4.100")]
    [InlineData("v1.4.99")]
    [InlineData("v1.3.500")]
    [InlineData("staging")]
    [InlineData("v1.5.0-rc1")]
    public async Task The_same_an_older_or_an_unversioned_release_is_not(string tag)
    {
        var check = new Rig(new StubFeed(Stable(tag))).Check;

        Assert.Equal(UpdateCheckOutcome.UpToDate, await check.CheckNowAsync(UpdateChannel.Stable));
        Assert.Null(check.Available);
    }

    [Fact]
    public async Task No_answer_is_reported_as_such_and_not_remembered()
    {
        var rig = new Rig(new StubFeed(lookup: ReleaseLookup.NoAnswer));

        Assert.Equal(UpdateCheckOutcome.NoAnswer, await rig.Check.CheckNowAsync(UpdateChannel.Stable));
        Assert.False(rig.Files.FileExists(RecordPath));
    }

    [Fact]
    public async Task The_daily_check_asks_once_a_day_and_keeps_showing_the_answer()
    {
        var feed = new StubFeed(Stable("v1.4.107"));
        var rig = new Rig(feed);
        await rig.Check.CheckDailyAsync();

        // A second start the same day: no request, the update still shown.
        var again = rig.Restart();
        await again.CheckDailyAsync();

        Assert.Equal(1, feed.Calls);
        Assert.Equal("1.4.107", again.Available!.Label);
    }

    [Fact]
    public async Task The_next_day_it_asks_again()
    {
        var feed = new StubFeed(Stable("v1.4.107"));
        var rig = new Rig(feed);
        await rig.Check.CheckDailyAsync();

        rig.Clock.Now = rig.Clock.Now.AddDays(1);
        await rig.Restart().CheckDailyAsync();

        Assert.Equal(2, feed.Calls);
    }

    [Fact]
    public async Task A_remembered_release_already_installed_is_not_offered()
    {
        var rig = new Rig(new StubFeed(Stable("v1.4.107")));
        await rig.Check.CheckDailyAsync();

        var upgraded = rig.Restart(current: new Version(1, 4, 107, 0));
        await upgraded.CheckDailyAsync();

        Assert.Null(upgraded.Available);
    }

    [Fact]
    public async Task Switching_the_channel_asks_again_the_same_day()
    {
        var feed = new StubFeed(Stable("v1.4.107"));
        var rig = new Rig(feed);
        await rig.Check.CheckDailyAsync();

        rig.Settings.UpdateChannel = UpdateChannel.Staging;
        await rig.Restart().CheckDailyAsync();

        Assert.Equal(2, feed.Calls);
        Assert.Equal(UpdateChannel.Staging, feed.LastChannel);
    }

    [Fact]
    public async Task Switched_off_the_daily_check_does_not_ask_but_the_button_does()
    {
        var feed = new StubFeed(Stable("v9.0.0"));
        var rig = new Rig(feed);
        rig.Settings.CheckForUpdates = false;

        await rig.Check.CheckDailyAsync();
        Assert.Equal(0, feed.Calls);

        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, await rig.Check.CheckNowAsync(UpdateChannel.Stable));
    }

    [Fact]
    public async Task A_staging_build_from_another_commit_is_offered()
    {
        var check = new Rig(new StubFeed(Staging(OtherCommit))).Check;

        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, await check.CheckNowAsync(UpdateChannel.Staging));
        Assert.Equal("staging f7219cf", check.Available!.Label);
        Assert.Equal(StagingPage, check.Available.PageUrl);
    }

    [Fact]
    public async Task The_staging_build_already_running_is_not()
    {
        var check = new Rig(new StubFeed(Staging(RunningCommit))).Check;

        Assert.Equal(UpdateCheckOutcome.UpToDate, await check.CheckNowAsync(UpdateChannel.Staging));
    }

    [Fact]
    public async Task Finding_an_update_tells_the_top_bar()
    {
        var check = new Rig(new StubFeed(Stable("v1.4.107"))).Check;
        int changes = 0;
        check.Changed += () => changes++;

        await check.CheckNowAsync(UpdateChannel.Stable);
        await check.CheckNowAsync(UpdateChannel.Stable);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void The_commit_is_read_from_the_informational_version()
    {
        Assert.Equal(RunningCommit, UpdateCheck.CommitOf("1.4.113+" + RunningCommit));
        Assert.Null(UpdateCheck.CommitOf("1.4.113"));
        Assert.Null(UpdateCheck.CommitOf(null));
    }

    [Fact]
    public void The_four_part_assembly_version_matches_a_three_part_tag()
    {
        Assert.Equal(new Version(1, 4, 0), UpdateCheck.ParseTag("v1.4"));
        Assert.Equal(new Version(1, 4, 107), UpdateCheck.ParseTag("V1.4.107"));
    }

    [Fact]
    public async Task The_feed_reads_a_stable_release()
    {
        var feed = Feed(HttpStatusCode.OK,
            $$"""{"tag_name":"v1.4.107","html_url":"{{Page}}","target_commitish":"{{OtherCommit}}","prerelease":false}""");

        var lookup = await feed.LatestAsync(UpdateChannel.Stable);

        Assert.True(lookup.Answered);
        Assert.Equal(new ReleaseInfo("v1.4.107", Page, OtherCommit), lookup.Release);
    }

    [Fact]
    public async Task The_stable_feed_skips_a_pre_release_and_the_staging_feed_takes_it()
    {
        string json = $$"""{"tag_name":"staging","html_url":"{{StagingPage}}","target_commitish":"{{OtherCommit}}","prerelease":true}""";

        Assert.Null((await Feed(HttpStatusCode.OK, json).LatestAsync(UpdateChannel.Stable)).Release);
        Assert.Equal(OtherCommit, (await Feed(HttpStatusCode.OK, json).LatestAsync(UpdateChannel.Staging)).Release!.Commit);
    }

    [Fact]
    public async Task The_staging_feed_asks_for_the_staging_tag()
    {
        string? asked = null;
        var feed = new GitHubReleaseFeed(new TestInstallation().Log, Current, new StubHandler(request =>
        {
            asked = request.RequestUri!.AbsolutePath;
            return Respond(HttpStatusCode.NotFound, "{}");
        }));

        await feed.LatestAsync(UpdateChannel.Staging);

        Assert.EndsWith("/releases/tags/staging", asked);
    }

    [Fact]
    public async Task Nothing_released_yet_is_an_answer()
    {
        var lookup = await Feed(HttpStatusCode.NotFound, """{"message":"Not Found"}""").LatestAsync(UpdateChannel.Stable);

        Assert.True(lookup.Answered);
        Assert.Null(lookup.Release);
    }

    [Fact]
    public async Task No_network_is_no_answer_and_nothing_in_the_log()
    {
        var log = new TestInstallation().Log;
        var feed = new GitHubReleaseFeed(log, Current, new StubHandler(_ => throw new HttpRequestException("offline")));

        Assert.False((await feed.LatestAsync(UpdateChannel.Stable)).Answered);
        Assert.Equal("", log.Text);
    }

    [Fact]
    public async Task A_broken_answer_is_logged()
    {
        var log = new TestInstallation().Log;
        var feed = new GitHubReleaseFeed(log, Current, new StubHandler(_ => Respond(HttpStatusCode.OK, "<html>")));

        Assert.False((await feed.LatestAsync(UpdateChannel.Stable)).Answered);
        Assert.Contains("update check", log.Text);
    }

    private static ReleaseLookup Stable(string tag) => ReleaseLookup.Found(new ReleaseInfo(tag, Page));

    private static ReleaseLookup Staging(string commit) =>
        ReleaseLookup.Found(new ReleaseInfo("staging", StagingPage, commit));

    private static GitHubReleaseFeed Feed(HttpStatusCode status, string body) =>
        new(new TestInstallation().Log, Current, new StubHandler(_ => Respond(status, body)));

    private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    /// <summary>One installation's worth of update checking, restartable on the same disk.</summary>
    private sealed class Rig
    {
        private readonly IReleaseFeed _feed;
        private readonly TestInstallation _installation;

        public Rig(IReleaseFeed feed)
        {
            _feed = feed;
            _installation = new TestInstallation(Files, new DateTime(2026, 10, 4, 9, 0, 0));
            Check = Restart();
        }

        public InMemoryFileSystem Files { get; } = new();

        public SimulatorSettings Settings { get; } = new();

        public FixedClock Clock => _installation.Clock;

        public UpdateCheck Check { get; }

        public UpdateCheck Restart(Version? current = null) =>
            new(Settings, _feed, current ?? Current, RunningCommit, Files, Clock, _installation.Log, RecordPath);
    }

    private sealed class StubFeed(ReleaseLookup lookup) : IReleaseFeed
    {
        public int Calls { get; private set; }

        public UpdateChannel? LastChannel { get; private set; }

        public Task<ReleaseLookup> LatestAsync(UpdateChannel channel, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastChannel = channel;
            return Task.FromResult(lookup);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                               CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
