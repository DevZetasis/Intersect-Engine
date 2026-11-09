using DarkUI.Controls;
using Intersect.Editor.Localization;
using Intersect.Framework.Core.GameObjects.Monsters;

namespace Intersect.Editor.Forms.Controls;

/// <summary>Edits a species draft's learnset only. No teaching or individual monster state.</summary>
public sealed class MonsterLearnsetEditor : UserControl
{
    private readonly ListBox _rows = new()
    {
        Dock = DockStyle.Top, Height = 150, IntegralHeight = false,
        BackColor = System.Drawing.Color.FromArgb(60, 63, 65), ForeColor = System.Drawing.Color.Gainsboro,
    };
    private readonly DarkComboBox _move = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly DarkNumericUpDown _level = new() { Minimum = 1, Maximum = int.MaxValue, Value = 1, Dock = DockStyle.Top };
    private readonly DarkButton _add = new() { Text = Strings.MonsterMoveEditor.Add, MinimumSize = new Size(65, 26), AutoSize = true };
    private readonly DarkButton _remove = new() { Text = Strings.MonsterMoveEditor.Remove, MinimumSize = new Size(65, 26), AutoSize = true };
    private List<MonsterLearnsetEntry> _entries = new();
    private bool _loading;
    private sealed record MoveChoice(Guid Id, string Name) { public override string ToString() => Name; }
    private sealed record LearnsetRow(MonsterLearnsetEntry Entry, string Text) { public override string ToString() => Text; }
    private MonsterLearnsetEntry? SelectedEntry => (_rows.SelectedItem as LearnsetRow)?.Entry;

    public MonsterLearnsetEditor()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(_rows, 0, 0);
        layout.SetColumnSpan(_rows, 2);
        layout.Controls.Add(new DarkLabel { Text = Strings.MonsterMoveEditor.Move, AutoSize = true }, 0, 1);
        layout.Controls.Add(_move, 1, 1);
        layout.Controls.Add(new DarkLabel { Text = Strings.MonsterMoveEditor.LearnLevel, AutoSize = true }, 0, 2);
        layout.Controls.Add(_level, 1, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        var newEntry = new DarkButton { Text = Strings.MonsterMoveEditor.NewEntry, MinimumSize = new Size(75, 26), AutoSize = true };
        newEntry.Click += (_, _) => { _rows.ClearSelected(); _level.Value = 1; _move.SelectedIndex = 0; };
        actions.Controls.Add(newEntry);
        actions.Controls.Add(_add);
        actions.Controls.Add(_remove);
        layout.Controls.Add(actions, 0, 3);
        layout.SetColumnSpan(actions, 2);
        var help = new DarkLabel { Text = Strings.MonsterMoveEditor.LearnsetHelp, Height = 55, Dock = DockStyle.Top };
        layout.Controls.Add(help, 0, 4);
        layout.SetColumnSpan(help, 2);
        Controls.Add(layout);
        _rows.SelectedIndexChanged += (_, _) => ShowSelected();
        _level.ValueChanged += (_, _) => EditSelected();
        _move.SelectedIndexChanged += (_, _) => EditSelected();
        _add.Click += (_, _) => AddEntry();
        _remove.Click += (_, _) => RemoveEntry();
    }
    public void Bind(List<MonsterLearnsetEntry> entries)
    {
        _entries = entries;
        RefreshChoices();
        ReloadRows();
    }
    private void RefreshChoices()
    {
        var id = SelectedEntry?.MoveId ?? (_move.SelectedItem as MoveChoice)?.Id ?? Guid.Empty;
        _loading = true;
        try
        {
            _move.Items.Clear();
            _move.Items.Add(new MoveChoice(Guid.Empty, Strings.General.None));
            _move.Items.AddRange(MonsterMoveDescriptor.Lookup.Values.Cast<MonsterMoveDescriptor>().OrderBy(move => move.Name)
                .Select(move => new MoveChoice(move.Id, move.Name)).ToArray());
            SelectMove(id);
        }
        finally { _loading = false; }
    }
    private void SelectMove(Guid id)
    {
        if (!_move.Items.Cast<MoveChoice>().Any(choice => choice.Id == id))
            _move.Items.Add(new MoveChoice(id, Strings.MonsterMoveEditor.Missing.ToString(id)));
        _move.SelectedItem = _move.Items.Cast<MoveChoice>().First(choice => choice.Id == id);
    }
    private void ReloadRows(MonsterLearnsetEntry? selected = null)
    {
        _loading = true;
        _rows.BeginUpdate();
        try
        {
            _rows.Items.Clear();
            foreach (var entry in _entries.OrderBy(entry => entry.Level))
            {
                var name = MonsterMoveDescriptor.Get(entry.MoveId)?.Name ?? Strings.MonsterMoveEditor.Missing.ToString(entry.MoveId);
                var row = new LearnsetRow(entry, $"{entry.Level}: {name}");
                _rows.Items.Add(row);
                if (ReferenceEquals(entry, selected)) _rows.SelectedItem = row;
            }
        }
        finally { _rows.EndUpdate(); _loading = false; }
        ShowSelected();
    }
    private void ShowSelected()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            if (SelectedEntry is { } entry)
            {
                _level.Value = entry.Level;
                SelectMove(entry.MoveId);
            }
            _remove.Enabled = SelectedEntry != null;
            _add.Enabled = (_move.SelectedItem as MoveChoice)?.Id is { } id && id != Guid.Empty && MonsterMoveDescriptor.Get(id) != null;
        }
        finally { _loading = false; }
    }
    private void EditSelected()
    {
        if (_loading) return;
        var id = (_move.SelectedItem as MoveChoice)?.Id ?? Guid.Empty;
        _add.Enabled = id != Guid.Empty && MonsterMoveDescriptor.Get(id) != null;
        if (SelectedEntry is not { } entry) return;
        // Selecting None for an existing row is not a deletion; use Remove explicitly.
        if (id == Guid.Empty)
        {
            _loading = true;
            try { SelectMove(entry.MoveId); }
            finally { _loading = false; }
            return;
        }
        entry.MoveId = id;
        entry.Level = (int)_level.Value;
        ReloadRows(entry);
    }
    private void AddEntry()
    {
        var id = (_move.SelectedItem as MoveChoice)?.Id ?? Guid.Empty;
        if (id == Guid.Empty || MonsterMoveDescriptor.Get(id) == null) return;
        var entry = new MonsterLearnsetEntry { MoveId = id, Level = (int)_level.Value };
        _entries.Add(entry);
        ReloadRows();
    }
    private void RemoveEntry()
    {
        if (SelectedEntry is not { } entry) return;
        _entries.Remove(entry);
        ReloadRows();
    }
}
