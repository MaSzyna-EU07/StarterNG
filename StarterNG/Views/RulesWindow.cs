using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using StarterNG.Classes;
using StarterNG.Domain;
using StarterNG.Infrastructure;

namespace StarterNG.Views;

public sealed class RulesWindow : Window
{
    private readonly List<List<string>> _rules = new();
    private readonly ListBox _ruleList = new() { Classes = { "Compact" } };
    private readonly WrapPanel _members = new() { Orientation = Orientation.Horizontal };
    private readonly ComboBox _category = new();
    private readonly ComboBox _model = new();
    private readonly VehicleDatabase _db = GameData.Instance.Vehicles;

    public RulesWindow()
    {
        Title = App.Loc["RandomizeTexturesRules"];
        Width = 720;
        Height = 460;
        MinWidth = 560;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        LoadRules();
        BuildCategories();

        _ruleList.SelectionChanged += (_, _) => ShowMembers();

        var add = new Button
        {
            Content = App.Loc["RuleAdd"],
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        add.Classes.Add("Flat");
        add.Click += (_, _) => AddToRule();

        _category.SelectionChanged += (_, _) => FillModels();

        var addRow = new Grid
        {
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
            ColumnDefinitions = new ColumnDefinitions("*,6,*,6,Auto")
        };
        _category.MinWidth = 0;
        _model.MinWidth = 0;
        _category.HorizontalAlignment = HorizontalAlignment.Stretch;
        _model.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(_category, 0);
        Grid.SetColumn(_model, 2);
        Grid.SetColumn(add, 4);
        addRow.Children.Add(_category);
        addRow.Children.Add(_model);
        addRow.Children.Add(add);

        var save = new Button
        {
            Content = App.Loc["RandomizeTexturesRulesSave"],
            MinWidth = 110,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        save.Classes.Add("Accent");
        save.Click += async (_, _) => { if (await SaveRules()) Close(); };

        var cancel = new Button
        {
            Content = App.Loc["Cancel"],
            MinWidth = 90,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        cancel.Classes.Add("Flat");
        cancel.Click += (_, _) => Close();

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 10, 0, 0),
            Children = { cancel, save }
        };

        var right = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(addRow, Dock.Bottom);
        DockPanel.SetDock(footer, Dock.Bottom);
        right.Children.Add(footer);
        right.Children.Add(addRow);
        right.Children.Add(new ScrollViewer
        {
            Content = _members,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        var grid = new Grid
        {
            Margin = new Avalonia.Thickness(10),
            ColumnDefinitions = new ColumnDefinitions("260,10,*")
        };
        Grid.SetColumn(_ruleList, 0);
        Grid.SetColumn(right, 2);
        grid.Children.Add(_ruleList);
        grid.Children.Add(right);

        Content = grid;

        RefreshList();
    }

    private void LoadRules()
    {
        string path = TexRandomizer.RulesPath;
        try
        {
            if (File.Exists(path))
                foreach (string line in File.ReadAllLines(path))
                {
                    var members = line.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => x.Length > 0)
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (members.Count > 0)
                        _rules.Add(members);
                }
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"reguly.txt {path}", ex);
        }

        _rules.Add(new List<string>());
    }

    private async System.Threading.Tasks.Task<bool> SaveRules()
    {
        string path = TexRandomizer.RulesPath;
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllLines(path,
                _rules.Where(r => r.Count > 0).Select(r => string.Join(",", r)));
            return true;
        }
        catch (Exception ex)
        {
            await Diagnostics.ReportAsync(path, ex);
            return false;
        }
    }

    private void RefreshList()
    {
        int keep = _ruleList.SelectedIndex;
        _ruleList.Items.Clear();

        foreach (var rule in _rules)
            _ruleList.Items.Add(new ListBoxItem
            {
                Content = new TextBlock
                {
                    Text = rule.Count == 0 ? App.Loc["RuleNew"] : string.Join(" ", rule),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Opacity = rule.Count == 0 ? 0.6 : 1
                }
            });

        _ruleList.SelectedIndex = keep >= 0 && keep < _rules.Count ? keep : 0;
        ShowMembers();
    }

    private void ShowMembers()
    {
        _members.Children.Clear();
        int idx = _ruleList.SelectedIndex;
        if (idx < 0 || idx >= _rules.Count) return;

        foreach (string member in _rules[idx])
        {
            string name = member;
            var btn = new Button
            {
                Content = name + "   ✕",
                FontSize = 12,
                Margin = new Avalonia.Thickness(0, 0, 4, 4),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            btn.Classes.Add("Flat");
            ToolTip.SetTip(btn, App.Loc["RuleRemove"]);
            btn.Click += (_, _) =>
            {
                _rules[idx].Remove(name);
                RefreshList();
            };
            _members.Children.Add(btn);
        }
    }

    private void BuildCategories()
    {
        var cats = _db.Textures
            .Select(t => t.ResolvedCategory)
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (string? c in cats)
            _category.Items.Add(new ComboBoxItem { Content = c, Tag = c });

        if (_category.Items.Count > 0)
            _category.SelectedIndex = 0;
        FillModels();
    }

    private void FillModels()
    {
        _model.Items.Clear();
        string? cat = (_category.SelectedItem as ComboBoxItem)?.Tag as string;

        var models = _db.Textures
            .Where(t => cat == null ||
                        string.Equals(t.ResolvedCategory, cat, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Model ?? "")
            .Where(m => m.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(m => m, StringComparer.OrdinalIgnoreCase);

        foreach (string m in models)
            _model.Items.Add(m);

        if (_model.Items.Count > 0)
            _model.SelectedIndex = 0;
    }

    private void AddToRule()
    {
        int idx = _ruleList.SelectedIndex;
        if (idx < 0 || idx >= _rules.Count) return;
        if (_model.SelectedItem is not string model || model.Length == 0) return;

        if (_rules[idx].Contains(model, StringComparer.OrdinalIgnoreCase))
            return;

        _rules[idx].Add(model);
        _rules[idx].Sort(StringComparer.OrdinalIgnoreCase);

        if (idx == _rules.Count - 1)
            _rules.Add(new List<string>());

        RefreshList();
    }
}
