using StarterNG.Infrastructure;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class WineConfigLinkTests
{
    // Rooted the way the host roots a path: the link is made to the folder's full
    // path, and on a Windows host "/home" alone is not one. Wine itself only runs off
    // Windows, but what is decided here is the same wherever these tests run.
    private static readonly string Home = Path.GetFullPath("/home/kolejarz");
    private static readonly string Config = Path.GetFullPath(Home + "/.config/MaSzyna");
    private static readonly string Roaming = Home + "/.wine/drive_c/users/kolejarz/AppData/Roaming";
    private static readonly string PrefixFolder = Roaming + "/MaSzyna";

    private static (WineConfigLink Link, TestInstallation Installation) Rig(Action<InMemoryFileSystem>? arrange = null)
    {
        var files = new InMemoryFileSystem(caseSensitive: true)
            .WithFile(Config + "/eu07.ini", "fpslimit 30\n");
        arrange?.Invoke(files);
        var installation = new TestInstallation(files);
        installation.Environment.With("HOME", Home).With("USER", "kolejarz");
        return (new WineConfigLink(files, installation.Environment, installation.SettingsPaths, installation.Log),
                installation);
    }

    [Fact]
    public void The_prefixs_own_folder_is_kept_aside_and_linked_to_the_starters()
    {
        var (link, installation) = Rig(files => files
            .WithFile(PrefixFolder + "/eu07.ini", "fpslimit 0\n")
            .WithFile(PrefixFolder + "/eu07_input-uart.ini", "port COM3\n"));

        link.Ensure();

        var files = installation.Files;
        Assert.Equal(Config, files.LinkTarget(PrefixFolder));
        Assert.Equal("fpslimit 0\n", files.ReadAllText(PrefixFolder + ".before-starter/eu07.ini"));
        // What only the prefix had comes along; the starter's own eu07.ini stays.
        Assert.Equal("port COM3\n", files.ReadAllText(Config + "/eu07_input-uart.ini"));
        Assert.Equal("fpslimit 30\n", files.ReadAllText(Config + "/eu07.ini"));
        Assert.Contains("now links to", installation.Log.Text);
    }

    [Fact]
    public void A_prefix_already_linked_is_left_alone()
    {
        var (link, installation) = Rig(files => files.WithDirectory(PrefixFolder));
        link.Ensure();

        link.Ensure();

        Assert.Equal(Config, installation.Files.LinkTarget(PrefixFolder));
        Assert.False(installation.Files.DirectoryExists(PrefixFolder + ".before-starter-2"));
    }

    [Fact]
    public void A_link_of_the_players_own_is_not_touched()
    {
        var (link, installation) = Rig(files =>
        {
            files.WithDirectory(Roaming);
            files.CreateDirectoryLink(PrefixFolder, "/mnt/shared/MaSzyna");
        });

        link.Ensure();

        Assert.Equal("/mnt/shared/MaSzyna", installation.Files.LinkTarget(PrefixFolder));
        Assert.Contains("left as it is", installation.Log.Text);
    }

    [Fact]
    public void Without_a_prefix_nothing_is_made()
    {
        var (link, installation) = Rig();

        link.Ensure();

        Assert.False(installation.Files.DirectoryExists(Home + "/.wine"));
        Assert.Contains("no prefix", installation.Log.Text);
    }

    [Fact]
    public void The_prefix_named_in_wineprefix_is_the_one_linked()
    {
        string prefix = Path.GetFullPath("/games/wine-maszyna");
        var (link, installation) = Rig(files => files
            .WithDirectory(prefix + "/drive_c/users/kolejarz/AppData/Roaming")
            .WithDirectory(Roaming));
        installation.Environment.With("WINEPREFIX", prefix);

        link.Ensure();

        Assert.Equal(Config, installation.Files.LinkTarget(prefix + "/drive_c/users/kolejarz/AppData/Roaming/MaSzyna"));
        Assert.Null(installation.Files.LinkTarget(PrefixFolder));
    }

    [Fact]
    public void An_older_backup_is_not_overwritten()
    {
        var (link, installation) = Rig(files => files
            .WithFile(PrefixFolder + ".before-starter/eu07.ini", "first\n")
            .WithFile(PrefixFolder + "/eu07.ini", "second\n"));

        link.Ensure();

        Assert.Equal("first\n", installation.Files.ReadAllText(PrefixFolder + ".before-starter/eu07.ini"));
        Assert.Equal("second\n", installation.Files.ReadAllText(PrefixFolder + ".before-starter-2/eu07.ini"));
    }

    [Fact]
    public void An_older_wine_keeps_application_data_where_xp_did()
    {
        string applicationData = Home + "/.wine/drive_c/users/kolejarz/Application Data";
        var (link, installation) = Rig(files => files.WithDirectory(applicationData));

        link.Ensure();

        Assert.Equal(Config, installation.Files.LinkTarget(applicationData + "/MaSzyna"));
    }

    [Fact]
    public void The_only_profile_is_used_when_the_user_name_differs()
    {
        string profile = Home + "/.wine/drive_c/users/steamuser/AppData/Roaming";
        var (link, installation) = Rig(files => files
            .WithDirectory(Home + "/.wine/drive_c/users/Public")
            .WithDirectory(profile));

        link.Ensure();

        Assert.Equal(Config, installation.Files.LinkTarget(profile + "/MaSzyna"));
    }

    [Fact]
    public void A_link_that_cannot_be_made_leaves_the_prefix_as_it_was()
    {
        var (link, installation) = Rig(files => files
            .WithFile(PrefixFolder + "/eu07.ini", "fpslimit 0\n")
            .WithFile(PrefixFolder + "/eu07_input-uart.ini", "port COM3\n"));
        var files = installation.Files;
        files.LinksFail = true;

        link.Ensure();

        Assert.Null(files.LinkTarget(PrefixFolder));
        Assert.Equal("fpslimit 0\n", files.ReadAllText(PrefixFolder + "/eu07.ini"));
        Assert.False(files.DirectoryExists(PrefixFolder + ".before-starter"));
        // Nothing carried over stays behind for a move that did not happen.
        Assert.False(files.FileExists(Config + "/eu07_input-uart.ini"));
        Assert.Equal("fpslimit 30\n", files.ReadAllText(Config + "/eu07.ini"));
        Assert.Contains("linking the settings", installation.Log.Text);
    }

    [Fact]
    public void Attempts_that_fail_do_not_pile_up_backups()
    {
        var (link, installation) = Rig(files => files.WithFile(PrefixFolder + "/eu07.ini", "fpslimit 0\n"));
        var files = installation.Files;
        files.LinksFail = true;

        link.Ensure();
        link.Ensure();
        link.Ensure();

        Assert.False(files.DirectoryExists(PrefixFolder + ".before-starter"));
        Assert.False(files.DirectoryExists(PrefixFolder + ".before-starter-2"));

        files.LinksFail = false;
        link.Ensure();

        Assert.Equal(Config, files.LinkTarget(PrefixFolder));
        Assert.Equal("fpslimit 0\n", files.ReadAllText(PrefixFolder + ".before-starter/eu07.ini"));
        Assert.False(files.DirectoryExists(PrefixFolder + ".before-starter-2"));
    }

    [Fact]
    public void A_copy_that_fails_half_way_takes_back_what_it_had_copied()
    {
        var (link, installation) = Rig(files => files
            .WithFile(PrefixFolder + "/a.ini", "first\n")
            .WithFile(PrefixFolder + "/b.ini", "second\n")
            // It is read from where the folder has been moved to by then.
            .FailReading(PrefixFolder + ".before-starter/b.ini"));
        var files = installation.Files;

        link.Ensure();

        Assert.Null(files.LinkTarget(PrefixFolder));
        Assert.Equal("first\n", files.ReadAllText(PrefixFolder + "/a.ini"));
        Assert.False(files.FileExists(Config + "/a.ini"));
        Assert.False(files.DirectoryExists(PrefixFolder + ".before-starter"));
    }

    [Fact]
    public void A_folder_that_cannot_be_put_back_is_said_to_be_where_it_is()
    {
        var (link, installation) = Rig(files => files.WithFile(PrefixFolder + "/eu07.ini", "fpslimit 0\n"));
        var files = installation.Files;
        files.LinksFail = true;
        // In the way of the move back: made by whatever broke the link, say.
        files.MoveFails = path => path.EndsWith("/Roaming/MaSzyna");

        link.Ensure();

        Assert.Equal("fpslimit 0\n", files.ReadAllText(PrefixFolder + ".before-starter/eu07.ini"));
        Assert.Contains("stays at", installation.Log.Text);
    }
}
