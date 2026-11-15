using DarkUI.Controls;
using Intersect.Config;
using DarkUI.Forms;
using Intersect.Editor.Forms.Controls;
using Intersect.Editor.General;
using Intersect.Editor.Localization;
using Intersect.Editor.Networking;
using Intersect.Enums;
using Intersect.Framework.Core.GameObjects.Monsters;

namespace Intersect.Editor.Forms.Editors;

public sealed class FrmMonsterEncounterTable : EditorForm
{
    private readonly GameObjectList _tableList = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<Guid, MonsterEncounterTableDescriptor> _drafts = new();
    private readonly Dictionary<Guid, string> _originals = new();
    private readonly DarkTextBox _id = new() { ReadOnly = true };
    private readonly DarkTextBox _name = new();
    private readonly DarkTextBox _description = new() { Multiline = true, Height = 80, ScrollBars = ScrollBars.Vertical };
    private readonly CheckBox _enabled = new() { Text = "Enabled", AutoSize = true };
    private readonly DarkTextBox _folder = new();
    private readonly DarkNumericUpDown _rate = new() { Minimum = 0, Maximum = 100 };
    private readonly DarkNumericUpDown _cooldown = new() { Minimum = 0, Maximum = MonsterEncounterTableDescriptor.MaximumCooldown };
    private readonly DataGridView _entries = new()
    {
        Height = 225,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
    };
    private readonly DataGridViewComboBoxColumn _species = new()
    {
        HeaderText = "Species", DisplayMember = nameof(SpeciesChoice.Label), ValueMember = nameof(SpeciesChoice.Id),
        FillWeight = 180, SortMode = DataGridViewColumnSortMode.NotSortable,
    };
    private sealed record SpeciesChoice(Guid Id, string Label);
    private readonly TableLayoutPanel _fields = new() { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), AutoScroll = true };
    private MonsterEncounterTableDescriptor? _selected;
    private string? _copied;
    private bool _loading;

    public FrmMonsterEncounterTable()
    {
        Text = "Monster Encounter Table Editor";
        Size = new Size(1050, 740);
        MinimumSize = new Size(760, 580);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
        ForeColor = System.Drawing.Color.Gainsboro;
        _tableList.BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
        _tableList.ForeColor = ForeColor;
        _tableList.Init(() => { }, SelectTable, NewTable, CopyTable, UndoTable, PasteTable, DeleteTable);
        var listPanel = new Panel { Dock = DockStyle.Left, Width = 215, Padding = new Padding(8) };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        AddButton(tools, Strings.MonsterSpeciesEditor.New, NewTable);
        AddButton(tools, Strings.MonsterSpeciesEditor.Copy, CopyTable);
        AddButton(tools, Strings.MonsterSpeciesEditor.Paste, PasteTable);
        AddButton(tools, Strings.MonsterSpeciesEditor.Undo, UndoTable);
        AddButton(tools, Strings.MonsterSpeciesEditor.Delete, DeleteTable);
        AddButton(tools, "Duplicate Table", DuplicateTable);
        listPanel.Controls.Add(_tableList);
        listPanel.Controls.Add(tools);
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(Strings.MonsterMoveEditor.Id, _id);
        AddField(Strings.MonsterMoveEditor.Name, _name);
        AddField(Strings.MonsterMoveEditor.Description, _description);
        AddField("Folder", _folder);
        AddField("Availability", _enabled);
        AddField("Default rate (%)", _rate);
        AddField("Cooldown (steps)", _cooldown);
        AddField("Entries", _entries);
        _entries.BackgroundColor = BackColor;
        _entries.DefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
        _entries.DefaultCellStyle.ForeColor = ForeColor;
        _entries.Columns.Add(_species);
        foreach (var header in new[] { "Weight", "Min level", "Max level", "Chance" })
            _entries.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = header, ReadOnly = header == "Chance",
                SortMode = DataGridViewColumnSortMode.NotSortable,
            });
        _entries.CellValidating += ValidateCell;
        _entries.CellValueChanged += (_, _) => ReadEntries();
        _entries.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_entries.CurrentCell is DataGridViewComboBoxCell && _entries.IsCurrentCellDirty)
                _entries.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _entries.DataError += (_, e) => e.ThrowException = false;
        var entryTools = new FlowLayoutPanel { AutoSize = true };
        AddButton(entryTools, "Add entry", AddEntry);
        AddButton(entryTools, "Remove entry", RemoveEntry);
        AddField("", entryTools);
        _folder.TextChanged += EditFields;
        _rate.ValueChanged += EditFields;
        _cooldown.ValueChanged += EditFields;
        _enabled.CheckedChanged += EditFields;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5) };
        _btnCancel = AddButton(buttons, Strings.MonsterSpeciesEditor.Cancel, (_, _) => Close());
        _btnCancel.CausesValidation = false;
        _btnSave = AddButton(buttons, Strings.MonsterSpeciesEditor.Save, SaveTables);
        Controls.Add(_fields);
        Controls.Add(listPanel);
        Controls.Add(buttons);
        _name.TextChanged += EditFields;
        _description.TextChanged += EditFields;
        FormClosed += (_, _) => Globals.CurrentEditor = -1;
    }
    private static DarkButton AddButton(Control parent, string text, EventHandler handler)
    {
        var button = new DarkButton { Text = text, AutoSize = true, MinimumSize = new Size(65, 26) };
        button.Click += handler;
        parent.Controls.Add(button);
        return button;
    }
    private void AddField(string label, Control control)
    {
        var row = _fields.RowCount++;
        _fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _fields.Controls.Add(new DarkLabel { Text = label, AutoSize = true, Margin = new Padding(0, 7, 8, 7) }, 0, row);
        control.Dock = DockStyle.Top;
        control.Margin = new Padding(0, 4, 0, 4);
        _fields.Controls.Add(control, 1, row);
    }
    public void InitEditor()
    {
        foreach (var id in _drafts.Keys.Where(id => MonsterEncounterTableDescriptor.Get(id) == null).ToArray())
        {
            _drafts.Remove(id);
            _originals.Remove(id);
        }
        var types = MonsterEncounterTableDescriptor.Lookup.Values.Cast<MonsterEncounterTableDescriptor>()
            .Select(type => _drafts.GetValueOrDefault(type.Id) ?? type).OrderBy(type => type.Name).ToArray();
        _tableList.Repopulate(types.Select(type => new KeyValuePair<Guid, KeyValuePair<string, string>>(type.Id, new(type.Enabled ? type.Name : type.Name + " (disabled)", type.Folder ?? ""))).ToArray(),
            types.Select(type => type.Folder).Where(folder => !string.IsNullOrEmpty(folder)).Distinct().Order().ToList(), false, false, string.Empty);
        if (_selected != null && MonsterEncounterTableDescriptor.Get(_selected.Id) == null) _selected = null;
        if (_selected == null && types.Length > 0)
        {
            _tableList.SelectedNode = _tableList.Nodes.Cast<TreeNode>()
                .SelectMany(node => node.Tag is Guid ? new[] { node } : node.Nodes.Cast<TreeNode>()).First();
            SelectTable((Guid)_tableList.SelectedNode.Tag);
        }
        ShowFields();
    }
    protected override void GameObjectUpdatedDelegate(GameObjectType type)
    {
        if (type == GameObjectType.MonsterSpecies) { ShowFields(); return; }
        if (type != GameObjectType.MonsterEncounterTable) return;
        foreach (var (id, draft) in _drafts)
            if (draft.JsonData == _originals[id] && MonsterEncounterTableDescriptor.Get(id) is { } source)
            {
                draft.Load(source.JsonData);
                draft.MakeBackup();
                _originals[id] = draft.JsonData;
            }
        InitEditor();
    }
    private void SelectTable(Guid id)
    {
        if (!MonsterEncounterTableDescriptor.TryGet(id, out var source)) return;
        if (!_drafts.TryGetValue(id, out var draft))
        {
            draft = new MonsterEncounterTableDescriptor(id);
            draft.Load(source.JsonData);
            draft.MakeBackup();
            _drafts.Add(id, draft);
            _originals.Add(id, draft.JsonData);
        }
        _selected = draft;
        ShowFields();
    }
    private void ShowFields()
    {
        _fields.Enabled = _selected != null;
        if (_selected == null) return;
        _loading = true;
        try
        {
            _id.Text = _selected.Id.ToString();
            _name.Text = _selected.Name;
            _description.Text = _selected.Description;
            _folder.Text = _selected.Folder;
            _enabled.Checked = _selected.Enabled;
            _rate.Value = Math.Clamp(_selected.Rate, 0, 100);
            _cooldown.Value = Math.Clamp(_selected.Cooldown, 0, MonsterEncounterTableDescriptor.MaximumCooldown);
            PopulateEntries();
        }
        finally { _loading = false; }
    }
    private void EditFields(object? sender, EventArgs e)
    {
        if (_loading || _selected == null) return;
        _selected.Name = _name.Text;
        _selected.Description = _description.Text;
        _selected.Folder = _folder.Text.Trim();
        _selected.Enabled = _enabled.Checked;
        _selected.Rate = (int)_rate.Value;
        _selected.Cooldown = (int)_cooldown.Value;
        _tableList.UpdateText(_selected.Enabled ? _selected.Name : _selected.Name + " (disabled)");
    }
    private void NewTable(object? sender, EventArgs e) => PacketSender.SendCreateObject(GameObjectType.MonsterEncounterTable);
    private void CopyTable(object? sender, EventArgs e) { if (_entries.EndEdit()) _copied = _selected?.JsonData; }
    private void PasteTable(object? sender, EventArgs e)
    {
        if (_selected == null || _copied == null) return;
        _selected.Load(_copied, keepCreationTime: true);
        InitEditor();
    }
    private void UndoTable(object? sender, EventArgs e) { _selected?.RestoreBackup(); InitEditor(); }
    private void DeleteTable(object? sender, EventArgs e)
    {
        if (_selected != null && DarkMessageBox.ShowWarning("Delete this encounter table?", Text,
                DarkDialogButton.YesNo, Icon) == DialogResult.Yes) PacketSender.SendDeleteObject(_selected);
    }
    private void SaveTables(object? sender, EventArgs e)
    {
        if (!_entries.EndEdit()) return;
        var changed = _drafts.Values.Where(type => type.JsonData != _originals[type.Id]).ToArray();
        foreach (var draft in changed)
        {
            if (draft.ValidationError(Options.Instance.Monsters.Battle.MaxLevel, id => MonsterSpeciesDescriptor.Get(id) != null) is not { } error)
                continue;
            DarkMessageBox.ShowWarning(error, Text);
            return;
        }
        foreach (var type in changed) PacketSender.SendSaveObject(type);
        Close();
    }

    private void DuplicateTable(object? sender, EventArgs e)
    {
        if (_selected == null || !_entries.EndEdit()) return;
        if (_selected.JsonData != _originals[_selected.Id])
        {
            DarkMessageBox.ShowWarning("Save the table, then reopen the editor to duplicate its saved settings.", Text);
            return;
        }
        PacketSender.SendCreateObject(GameObjectType.MonsterEncounterTable, _selected.Id);
    }

    private void PopulateEntries()
    {
        _entries.Rows.Clear();
        var choices = MonsterSpeciesDescriptor.Lookup.Values.Cast<MonsterSpeciesDescriptor>()
            .OrderBy(item => item.Name).Select(item => new SpeciesChoice(item.Id, item.Name)).ToList();
        var entries = _selected?.Entries ?? [];
        var known = choices.Select(item => item.Id).ToHashSet();
        foreach (var id in entries.Select(entry => entry?.SpeciesId ?? Guid.Empty).Distinct())
            if (known.Add(id)) choices.Add(new(id, $"Missing ({id})"));
        _species.DataSource = choices;
        foreach (var entry in entries)
            _entries.Rows.Add(entry?.SpeciesId ?? Guid.Empty, entry?.Weight ?? 0, entry?.MinimumLevel ?? 1, entry?.MaximumLevel ?? 1);
        UpdatePercentages();
    }

    private void AddEntry(object? sender, EventArgs e)
    {
        if (_selected == null || !_entries.EndEdit()) return;
        if (MonsterSpeciesDescriptor.Lookup.Values.Cast<MonsterSpeciesDescriptor>().OrderBy(item => item.Name).FirstOrDefault() is not { } species)
        {
            DarkMessageBox.ShowWarning("Create a monster species before adding an entry.", Text);
            return;
        }
        _selected.Entries ??= [];
        _selected.Entries.Add(new(species.Id, 1, 1, 1));
        ShowFields();
        _entries.CurrentCell = _entries.Rows[_entries.Rows.Count - 1].Cells[0];
    }

    private void RemoveEntry(object? sender, EventArgs e)
    {
        if (_selected == null || !_entries.EndEdit() || _entries.CurrentRow is not { } row) return;
        _selected.Entries.RemoveAt(row.Index);
        ShowFields();
    }

    private void ValidateCell(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (_loading || e.ColumnIndex is < 1 or > 3) return;
        var minimum = e.ColumnIndex == 1 ? 0 : 1;
        var maximum = e.ColumnIndex == 1 ? 100000 : Options.Instance.Monsters.Battle.MaxLevel;
        var invalid = !int.TryParse(e.FormattedValue?.ToString(), out var value) || value < minimum || value > maximum;
        _entries.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText =
            invalid ? $"Enter a whole number from {minimum} to {maximum}." : "";
    }

    private void ReadEntries()
    {
        if (_loading || _selected == null) return;
        _selected.Entries = _entries.Rows.Cast<DataGridViewRow>().Select(row => new MonsterEncounterEntry(
            row.Cells[0].Value is Guid id ? id : Guid.Empty, ReadNumber(row, 1), ReadNumber(row, 2), ReadNumber(row, 3))).ToList();
        UpdatePercentages();
    }

    private static int ReadNumber(DataGridViewRow row, int index) =>
        int.TryParse(row.Cells[index].Value?.ToString(), out var value) ? value : -1;

    private void UpdatePercentages()
    {
        if (_selected?.Entries is not { } entries) return;
        var total = entries.Sum(entry => (long)Math.Max(0, entry?.Weight ?? 0));
        var loading = _loading;
        _loading = true;
        try
        {
            for (var i = 0; i < entries.Count; i++)
                _entries.Rows[i].Cells[4].Value = total == 0 ? "0%" : $"{Math.Max(0, entries[i]?.Weight ?? 0) / (decimal)total:P1}";
        }
        finally { _loading = loading; }
    }
}
