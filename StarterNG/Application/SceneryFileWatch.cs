using System;
using System.Collections.Generic;
using System.Linq;
using StarterNG.Application.Abstractions;
using StarterNG.Classes;
using StarterNG.Domain.Sceneries;

namespace StarterNG.Application;

/// <summary>What became of a scenery's files since they were read.</summary>
/// <param name="Current">The scenery as it is held; null for a file new in the folder.</param>
/// <param name="Fresh">As its files have it now; null for one gone from the folder.</param>
public sealed record SceneryChange(Scenery? Current, Scenery? Fresh);

public enum SceneryVerdict
{
    /// <summary>Nothing to do about it, for now or for good.</summary>
    Leave,

    /// <summary>The library can take it in.</summary>
    Take,

    /// <summary>Taking it in loses edits, so that is for someone to say.</summary>
    AskFirst
}

/// <summary>What is to become of a change.</summary>
/// <param name="Lost">The consists with unsaved edits that <see cref="SceneryVerdict.AskFirst"/> is about.</param>
public sealed record SceneryJudgement(SceneryVerdict Verdict, IReadOnlyList<Trainset> Lost)
{
    public static readonly SceneryJudgement Leave = new(SceneryVerdict.Leave, Array.Empty<Trainset>());
    public static readonly SceneryJudgement Take = new(SceneryVerdict.Take, Array.Empty<Trainset>());
}

/// <summary>
/// Keeps the sceneries in step with their files when those change outside the
/// starter - in a text editor, in the simulator's editor - without anyone having
/// to ask for a reload.
/// </summary>
public sealed class SceneryFileWatch
{
    private readonly IFileSystem _files;
    private readonly ISceneryRepository _sceneries;
    private readonly GameLibrary _library;
    private readonly AppState _state;

    // Files that would not load, and how they stood then: not tried, and not logged,
    // again before something about them changes.
    private readonly Dictionary<string, string> _unreadable = new();

    private HashSet<string> _away = new();

    public SceneryFileWatch(IFileSystem files, ISceneryRepository sceneries, GameLibrary library, AppState state)
    {
        _files = files;
        _sceneries = sceneries;
        _library = library;
        _state = state;
    }

    /// <summary>What has changed among the files of <paramref name="held"/>. Off the UI thread, one look at a time.</summary>
    public IReadOnlyList<SceneryChange> Look(IReadOnlyList<Scenery> held)
    {
        var changes = new List<SceneryChange>();
        if (_sceneries.ListFiles() is not { } files)
            return changes;

        var there = files.ToHashSet();
        var away = new HashSet<string>();
        foreach (var scenery in held)
        {
            if (!there.Contains(scenery.Path))
            {
                // A file being replaced is away for a moment: gone only if still so next time.
                away.Add(scenery.Path);
                if (_away.Contains(scenery.Path))
                    changes.Add(new SceneryChange(scenery, null));
            }
            else if (IsStale(scenery) && LoadOnce(scenery.Path, scenery.Sources.Keys) is { } fresh)
            {
                changes.Add(new SceneryChange(scenery, fresh));
            }
        }
        _away = away;

        var known = held.Select(scenery => scenery.Path).ToHashSet();
        foreach (string file in files.Where(file => !known.Contains(file)))
            if (LoadOnce(file, new[] { file }) is { } added)
                changes.Add(new SceneryChange(null, added));

        return changes;
    }

    /// <summary>Whether any of the files it was read from is no longer as it was.</summary>
    public bool IsStale(Scenery scenery) =>
        scenery.Sources.Any(source => FileStamp.Of(_files, source.Key) != source.Value);

    private Scenery? LoadOnce(string path, IEnumerable<string> sources)
    {
        string stood = string.Join('|', sources.Select(source => FileStamp.Of(_files, source)));
        if (_unreadable.TryGetValue(path, out string? before) && before == stood)
            return null;

        var loaded = _sceneries.Load(path);
        if (loaded is null)
            _unreadable[path] = stood;
        else
            _unreadable.Remove(path);
        return loaded;
    }

    /// <summary>
    /// Puts what was held into the scenery read anew. A consist whose text is still
    /// what it was goes over as the object it is, with its edits and its undo steps;
    /// the choices made by hand go with them.
    /// </summary>
    /// <returns>The consists with unsaved edits that have no place there: their text changed too.</returns>
    public static IReadOnlyList<Trainset> CarryOver(Scenery current, Scenery fresh)
    {
        var waiting = current.Trainsets.ToList();
        for (int i = 0; i < fresh.Trainsets.Count; i++)
        {
            var read = fresh.Trainsets[i];
            int at = waiting.FindIndex(t => t.SourceFile == read.SourceFile && t.RawEntry == read.RawEntry);
            if (at < 0)
                continue;

            fresh.Trainsets[i] = waiting[at];
            waiting.RemoveAt(at);
        }

        fresh.Weather.KeepChoicesOf(current.Weather);
        foreach (var include in fresh.Includes)
            if (current.Includes.FirstOrDefault(i => i.FilePath == include.FilePath) is { } earlier)
                include.Selected = earlier.Selected;

        return waiting.Where(SceneryTrainsetText.IsModified).ToList();
    }

    /// <summary>
    /// On the UI thread, once for each change and just before acting on it: what a
    /// look found may be out of date by then. A scenery read anew has what was held
    /// put into it here.
    /// </summary>
    public SceneryJudgement Judge(SceneryChange change)
    {
        var sceneries = _library.Sceneries;
        if (change.Current is not { } current)
            return sceneries.Any(s => s.Path == change.Fresh!.Path) ? SceneryJudgement.Leave : SceneryJudgement.Take;

        // Saved from here since that look, or reloaded by hand.
        if (!sceneries.Contains(current) || (change.Fresh is not null && !IsStale(current)))
            return SceneryJudgement.Leave;

        // One whose file went stays with what was edited in it, for that to be saved elsewhere.
        if (change.Fresh is not { } fresh)
            return current.Trainsets.Any(SceneryTrainsetText.IsModified) ? SceneryJudgement.Leave : SceneryJudgement.Take;

        var lost = CarryOver(current, fresh);
        return lost.Count > 0 ? new SceneryJudgement(SceneryVerdict.AskFirst, lost) : SceneryJudgement.Take;
    }

    /// <summary>On the UI thread: the library, and what is picked in it, take the change in.</summary>
    public void Apply(SceneryChange change)
    {
        var sceneries = _library.Sceneries;
        if (change.Current is not { } current)
        {
            sceneries.Add(change.Fresh!);
            return;
        }

        if (change.Fresh is not { } fresh)
        {
            sceneries.Remove(current);
            if (ReferenceEquals(_state.CurrentScenery, current))
            {
                _state.CurrentScenery = null;
                _state.CurrentTrainset = null;
                _state.StartingVehicleName = null;
            }
            return;
        }

        sceneries[sceneries.IndexOf(current)] = fresh;
        if (ReferenceEquals(_state.CurrentScenery, current))
            Follow(current, fresh);
    }

    private void Follow(Scenery current, Scenery fresh)
    {
        // Its text changed, the one of its name stands for it, or the one in its place.
        var picked = _state.CurrentTrainset;
        var kept = picked is null || fresh.Trainsets.Contains(picked)
            ? picked
            : fresh.Trainsets.FirstOrDefault(t => t.Name == picked.Name)
              ?? fresh.Trainsets.ElementAtOrDefault(current.Trainsets.IndexOf(picked));

        _state.CurrentScenery = fresh;
        _state.CurrentTrainset = kept;
        if (kept is null || kept.Vehicles.All(v => v.Name != _state.StartingVehicleName))
            _state.StartingVehicleName = null;
    }
}
