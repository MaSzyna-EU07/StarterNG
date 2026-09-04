using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;

namespace StarterNG.Infrastructure;

public enum ShortcutScope
{
    /// <summary>Works anywhere in the window.</summary>
    Global,

    /// <summary>Only while the depot page is on screen.</summary>
    Depot,

    /// <summary>Handled by the consist strip itself, once a vehicle card has focus.</summary>
    Consist
}

/// <summary>
/// One row of the shortcut table. <see cref="Key"/> is <see cref="Avalonia.Input.Key.None"/>
/// for rows that only document a gesture handled elsewhere (the consist strip reads the
/// arrow keys itself, they are never dispatched from the window).
/// </summary>
public sealed record Shortcut(
    string Id,
    string Display,
    string LabelKey,
    ShortcutScope Scope,
    Key Key = Key.None,
    KeyModifiers Modifiers = KeyModifiers.None)
{
    public bool IsDispatched => Key != Key.None;

    public bool Matches(Key key, KeyModifiers modifiers) =>
        IsDispatched && Key == key && Modifiers == modifiers;
}

/// <summary>
/// The single source of truth for the starter's own shortcuts: the window dispatches from
/// this table and the cheat sheet is printed from it, so the two cannot drift apart. These
/// are deliberately hardcoded - the configurable key list in Settings belongs to the
/// simulator's cab controls and mixing the two only confuses people.
/// </summary>
public static class Shortcuts
{
    public const string Start = "start";
    public const string PageScenarios = "page.scenarios";
    public const string PageDepot = "page.depot";
    public const string PageSettings = "page.settings";
    public const string ShowHelp = "help";
    public const string AddVehicle = "vehicle.add";
    public const string RemoveVehicle = "vehicle.remove";
    public const string ClearConsist = "consist.clear";
    public const string SetNumber = "vehicle.number";
    public const string FocusSearch = "search";

    public static readonly IReadOnlyList<Shortcut> All = new[]
    {
        new Shortcut(Start, "Ctrl+Enter", "Start", ShortcutScope.Global,
            Key.Enter, KeyModifiers.Control),
        new Shortcut(PageScenarios, "Ctrl+1", "NavScenarios", ShortcutScope.Global,
            Key.D1, KeyModifiers.Control),
        new Shortcut(PageDepot, "Ctrl+2", "NavDepot", ShortcutScope.Global,
            Key.D2, KeyModifiers.Control),
        new Shortcut(PageSettings, "Ctrl+3", "NavSettings", ShortcutScope.Global,
            Key.D3, KeyModifiers.Control),
        new Shortcut(ShowHelp, "F1", "ShortcutShowHelp", ShortcutScope.Global,
            Key.F1),

        new Shortcut(FocusSearch, "Ctrl+F", "ShortcutFocusSearch", ShortcutScope.Depot,
            Key.F, KeyModifiers.Control),
        new Shortcut(AddVehicle, "Insert", "ShortcutAddVehicle", ShortcutScope.Depot,
            Key.Insert),
        new Shortcut(RemoveVehicle, "Delete", "RemoveVehicle", ShortcutScope.Depot,
            Key.Delete),
        new Shortcut(ClearConsist, "Ctrl+Delete", "ConsistRemoveAll", ShortcutScope.Depot,
            Key.Delete, KeyModifiers.Control),
        new Shortcut(SetNumber, "Ctrl+N", "ShortcutSetNumber", ShortcutScope.Depot,
            Key.N, KeyModifiers.Control),

        new Shortcut("consist.prevnext", "←  →", "ShortcutPrevNext", ShortcutScope.Consist),
        new Shortcut("consist.firstlast", "Home / End", "ShortcutFirstLast", ShortcutScope.Consist),
        new Shortcut("consist.member", "↑  ↓", "ShortcutMember", ShortcutScope.Consist),
        new Shortcut("consist.move", "Ctrl+←  Ctrl+→", "ShortcutMoveVehicle", ShortcutScope.Consist),
        new Shortcut("consist.flip", "Space", "TipFlip", ShortcutScope.Consist),
        new Shortcut("consist.menu", "Enter", "ShortcutCardMenu", ShortcutScope.Consist)
    };

    public static string? Match(Key key, KeyModifiers modifiers) =>
        All.FirstOrDefault(s => s.Matches(key, modifiers))?.Id;

    public static IEnumerable<Shortcut> InScope(ShortcutScope scope) =>
        All.Where(s => s.Scope == scope);

    public static string ScopeLabel(ShortcutScope scope) => scope switch
    {
        ShortcutScope.Depot => App.Loc["NavDepot"],
        ShortcutScope.Consist => App.Loc["Consist"],
        _ => App.Loc["ShortcutsGlobal"]
    };
}
