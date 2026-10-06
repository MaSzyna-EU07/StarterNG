using System.Text;
using StarterNG.Domain.Vehicles;
using StarterNG.Infrastructure.Vehicles;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class VehicleJsonTests
{
    private static readonly TexturesTxtParser Parser = new();

    private static readonly VehicleJsonSerializer Serializer = new();

    /// <summary>
    /// A vehicle folder exercising every piece of the legacy syntax at once: the
    /// headers and comments that carry no data, a category marker, a credit line
    /// with Polish text, an alternative model, a wreck and an archival section.
    /// </summary>
    private const string Ep07 = """
        # EP07 - lokomotywy elektryczne
        @ep07
        !=e
        ep07-135.mat=ep07.e3d,ep07 // 1.0,EP07-135,PKP Intercity,Warszawa Grochów,2019-04-01,Wrocław,Zażółć,Gęślą Jaźń
        ep07-174.mat=ep07.e3d,ep07=ep07p.e3d,ep07p
        ep07wrak.mat=ep07.e3d,ep07
        linia bez znaku rownosci
        $a
        ep07-stary.mat=ep07.e3d,ep07
        """;

    /// <summary>A unit whose cars are coupled into a fixed set.</summary>
    private const string En57 = """
        ^2
        en57-1000ra.mat=en57.e3d,en57
        en57-1000rb.mat=en57.e3d,en57
        """;

    [Fact]
    public void An_entry_written_out_and_read_back_is_the_entry_it_started_as()
    {
        var entry = Parser.Parse("pkp/ep07/", Lines(Ep07))!;

        var restored = Serializer.Read(Serializer.Write(entry));

        Assert.Equal(Describe(entry), Describe(restored!));
    }

    [Fact]
    public void The_catalogue_built_from_json_matches_the_one_built_from_textures_txt()
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Dynamic("pkp", "ep07"), Ep07)
            .WithLegacyFile(Dynamic("pkp", "en57"), En57);
        var installation = new TestInstallation(files);

        installation.Vehicles.Load(installation.Library.Vehicles);
        var fromJson = ReloadThroughJson(installation);

        Assert.Equal(5, installation.Library.Vehicles.Textures.Count);
        Assert.Equal(Describe(installation.Library.Vehicles), Describe(fromJson));
    }

    [Fact]
    public void Headers_and_unparsable_lines_survive_the_trip_so_nothing_is_dropped()
    {
        var entry = Parser.Parse("pkp/ep07/", Lines(Ep07))!;

        var restored = Serializer.Read(Serializer.Write(entry))!;

        Assert.Equal(new[] { "# EP07 - lokomotywy elektryczne", "@ep07", "linia bez znaku rownosci" },
                     restored.Unknown);
    }

    [Fact]
    public void Polish_text_is_written_as_itself_rather_than_as_escapes()
    {
        var entry = Parser.Parse("pkp/ep07/", Lines(Ep07))!;

        string json = Serializer.Write(entry);

        Assert.Contains("Warszawa Grochów", json);
        Assert.DoesNotContain("\\u", json);
    }

    [Fact]
    public void The_merged_database_is_read_before_the_packages_layered_on_it()
    {
        var entry = Parser.Parse("pkp/ep07/", Lines(Ep07))!;
        var package = Parser.Parse("pkp/en57/", Lines(En57))!;
        var files = new InMemoryFileSystem()
            .WithFile(Database("vehicles.json"), Serializer.WriteCollection(new[] { entry }))
            .WithFile(Database("aaa-pakiet.json"), Serializer.Write(package));
        var installation = new TestInstallation(files);
        var catalog = new VehicleCatalog(installation.MiniTextures);

        int liveries = Repository(installation).Load(catalog);

        Assert.Equal(6, liveries);
        Assert.Equal("legacy:pkp/ep07/ep07-135", catalog.Textures[0].Uuid);
    }

    [Fact]
    public void An_entry_from_a_newer_format_is_refused_instead_of_half_read()
    {
        var files = new InMemoryFileSystem()
            .WithFile(Database("z-przyszlosci.json"),
                      """{"uuid":"x","schema_version":99,"textures":[{"skinfile":"ep07-135"}]}""");
        var installation = new TestInstallation(files);
        var catalog = new VehicleCatalog(installation.MiniTextures);

        int liveries = Repository(installation).Load(catalog);

        Assert.Equal(0, liveries);
        Assert.Empty(catalog.Textures);
        Assert.Contains("z-przyszlosci.json", installation.Log.Text);
    }

    [Fact]
    public void An_unreadable_package_is_logged_and_leaves_the_rest_of_the_database_alone()
    {
        var entry = Parser.Parse("pkp/ep07/", Lines(Ep07))!;
        var files = new InMemoryFileSystem()
            .WithFile(Database("aaa-polamany.json"), "{ to nie jest json")
            .WithFile(Database("bbb-dobry.json"), Serializer.Write(entry));
        var installation = new TestInstallation(files);
        var catalog = new VehicleCatalog(installation.MiniTextures);

        int liveries = Repository(installation).Load(catalog);

        Assert.Equal(4, liveries);
        Assert.Contains("aaa-polamany.json", installation.Log.Text);
    }

    private static VehicleJsonRepository Repository(TestInstallation installation) =>
        new(installation.Files, installation.Paths, installation.Log, Serializer);

    /// <summary>
    /// Writes every vehicle folder of an installation out as its own package file
    /// and loads the catalogue back from those, which is the migration the starter
    /// will do silently once this format is the one it reads.
    /// </summary>
    private static VehicleCatalog ReloadThroughJson(TestInstallation installation)
    {
        foreach (string maker in installation.Files.GetDirectories(installation.Paths.Dynamic))
        foreach (string vehicle in installation.Files.GetDirectories(maker))
        {
            string path = Path.Combine(vehicle, "textures.txt");
            if (!installation.Files.FileExists(path))
                continue;

            string directory = $"{Path.GetFileName(maker)}/{Path.GetFileName(vehicle)}/";
            var entry = Parser.Parse(directory,
                                     installation.Files.ReadAllText(path, Encoding.GetEncoding(1250)).Split('\n'));
            if (entry is not null)
                installation.Files.WithFile(Database($"{directory.Replace('/', '.')}json"),
                                            Serializer.Write(entry));
        }

        var catalog = new VehicleCatalog(installation.MiniTextures);
        Repository(installation).Load(catalog);
        return catalog;
    }

    private static string[] Lines(string file) => file.Split('\n');

    private static string Dynamic(string maker, string vehicle) =>
        TestInstallation.At("dynamic", maker, vehicle, "textures.txt");

    private static string Database(string file) => TestInstallation.At("databases", "vehicles", file);

    /// <summary>Everything an entry carries, flattened so a difference reads as one.</summary>
    private static string Describe(VehicleEntry entry) =>
        string.Join('\n',
            new[] { $"entry {entry.Uuid}" }
                .Concat(entry.Groups.Select(Describe))
                .Concat(entry.Textures.Select(Describe))
                .Concat(entry.Sets.Select(Describe))
                .Concat(entry.Unknown.Select(line => $"unknown {line}")));

    /// <summary>The same for a whole catalogue, wrecks and resolved fields included.</summary>
    private static string Describe(VehicleCatalog catalog) =>
        string.Join('\n',
            catalog.GroupsById.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => Describe(pair.Value))
                .Concat(catalog.TextureByUuid.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                               .Select(pair => Describe(pair.Value)))
                .Concat(catalog.Sets.Select(Describe)));

    private static string Describe(VehicleGroup group) =>
        $"group {group.Id}|{group.Category}|{group.Mini}|{group.Archived}|{group.Implicit}";

    private static string Describe(VehicleSet set) =>
        $"set {set.Uuid}|{set.Mode}|{set.Count}|{string.Join(',', set.TextureRefs)}";

    private static string Describe(VehicleTexture texture) =>
        $"texture {texture.Uuid}|{texture.Directory}|{texture.Skinfile}|{texture.Model}|{texture.Group}|" +
        $"{texture.MiniRef}|{texture.TextureMini}|{texture.Wreck}|{texture.ResolvedClass}|" +
        $"{texture.ResolvedCategory}|{texture.ResolvedArchived}|{texture.Meta?.Raw}|" +
        string.Join(';', texture.Aliases.Select(alias => $"{alias.Model},{alias.MiniRef},{alias.TextureMini}"));
}
