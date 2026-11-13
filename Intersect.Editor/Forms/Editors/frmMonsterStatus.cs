using DarkUI.Controls;
using DarkUI.Forms;
using Intersect.Editor.Forms.Controls;
using Intersect.Editor.General;
using Intersect.Editor.Localization;
using Intersect.Editor.Networking;
using Intersect.Enums;
using Intersect.Framework.Core.GameObjects.Animations;
using Intersect.Framework.Core.GameObjects.Monsters;

namespace Intersect.Editor.Forms.Editors;

public sealed class FrmMonsterStatus : EditorForm
{
    private readonly GameObjectList _statusList = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<Guid, MonsterStatusDescriptor> _drafts = new();
    private readonly Dictionary<Guid, string> _originals = new();
    private readonly DarkTextBox _id = new() { ReadOnly = true };
    private readonly DarkTextBox _name = new();
    private readonly DarkTextBox _description = new() { Multiline = true, Height = 80, ScrollBars = ScrollBars.Vertical };
    private readonly Dictionary<Control, Action<MonsterStatusDescriptor>> _readFields = new();
    private readonly TableLayoutPanel _fields = new() { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), AutoScroll = true };
    private sealed record ContentChoice(Guid Id, string Name) { public override string ToString() => Name; }
    private MonsterStatusDescriptor? _selected;
    private string? _copied;
    private bool _loading;
    private static DarkComboBox Choice() => new() { DropDownStyle = ComboBoxStyle.DropDownList };

    public FrmMonsterStatus()
    {
        Text = "Monster Status Editor";
        Size = new Size(840, 700);
        MinimumSize = new Size(760, 580);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
        ForeColor = System.Drawing.Color.Gainsboro;
        _statusList.BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
        _statusList.ForeColor = ForeColor;
        _statusList.Init(() => { }, SelectStatus, NewStatus, CopyStatus, UndoStatus, PasteStatus, DeleteStatus);
        var listPanel = new Panel { Dock = DockStyle.Left, Width = 245, Padding = new Padding(8) };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        AddButton(tools, Strings.MonsterSpeciesEditor.New, NewStatus);
        AddButton(tools, Strings.MonsterSpeciesEditor.Copy, CopyStatus);
        AddButton(tools, Strings.MonsterSpeciesEditor.Paste, PasteStatus);
        AddButton(tools, Strings.MonsterSpeciesEditor.Undo, UndoStatus);
        AddButton(tools, Strings.MonsterSpeciesEditor.Delete, DeleteStatus);
        listPanel.Controls.Add(_statusList);
        listPanel.Controls.Add(tools);
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(Strings.MonsterMoveEditor.Id, _id);
        AddField(Strings.MonsterMoveEditor.Name, _name);
        AddField(Strings.MonsterMoveEditor.Description, _description);
        Flag("Persist outside battle", status => status.PersistOutsideBattle, (status, value) => status.PersistOutsideBattle = value);
        Flag("Remove on switching", status => status.RemoveWhenSwitching, (status, value) => status.RemoveWhenSwitching = value);
        SelectEnum<MonsterStatusGroup>("Exclusive group", status => status.Group, (status, value) => status.Group = value);
        var duration = SelectEnum<MonsterStatusDuration>("Duration", status => status.Duration, (status, value) => status.Duration = value);
        var minimumTurns = Number("Turns / minimum", 1, 999, status => status.MinimumTurns, (status, value) => status.MinimumTurns = (int)value);
        var maximumTurns = Number("Maximum turns (random)", 1, 999, status => status.MaximumTurns, (status, value) => status.MaximumTurns = (int)value);
        void SetDurationControls()
        {
            minimumTurns.Enabled = duration.SelectedItem is MonsterStatusDuration.Fixed or MonsterStatusDuration.Random;
            maximumTurns.Enabled = duration.SelectedItem is MonsterStatusDuration.Random;
        }
        duration.SelectedIndexChanged += (_, _) => SetDurationControls();
        SetDurationControls();
        Number("Recovery chance (%)", 0, 100, status => status.RecoveryChance, (status, value) => status.RecoveryChance = (int)value);
        Number("Block action chance (%)", 0, 100, status => status.PreventActionChance, (status, value) => status.PreventActionChance = (int)value);
        var message = new DarkTextBox();
        AddField("Blocked action text", message);
        _readFields[message] = status => message.Text = status.PreventActionMessage;
        message.TextChanged += (_, _) => { if (!_loading && _selected is { } status) status.PreventActionMessage = message.Text; };
        SelectEnum<MonsterStatusDamage>("Periodic damage", status => status.Damage, (status, value) => status.Damage = value);
        Number("Damage (HP or %)", 0, int.MaxValue, status => status.DamageAmount, (status, value) => status.DamageAmount = value, 4);
        Number("Attack retained (%)", 1, 100, status => status.AttackPercent, (status, value) => status.AttackPercent = (int)value);
        Number("Defense retained (%)", 1, 100, status => status.DefensePercent, (status, value) => status.DefensePercent = (int)value);
        Number("Sp. Attack retained (%)", 1, 100, status => status.SpecialAttackPercent, (status, value) => status.SpecialAttackPercent = (int)value);
        Number("Sp. Defense retained (%)", 1, 100, status => status.SpecialDefensePercent, (status, value) => status.SpecialDefensePercent = (int)value);
        Number("Speed retained (%)", 1, 100, status => status.SpeedPercent, (status, value) => status.SpeedPercent = (int)value);
        Animation("Applied animation", status => status.AppliedAnimationId, (status, id) => status.AppliedAnimationId = id);
        Animation("Activation animation", status => status.ActivationAnimationId, (status, id) => status.ActivationAnimationId = id);
        Animation("Recovery animation", status => status.RemovedAnimationId, (status, id) => status.RemovedAnimationId = id);
        var folder = new DarkTextBox();
        AddField("Folder", folder);
        _readFields[folder] = status => folder.Text = status.Folder;
        folder.TextChanged += (_, _) => { if (!_loading && _selected is { } status) status.Folder = folder.Text.Trim(); };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5) };
        _btnCancel = AddButton(buttons, Strings.MonsterSpeciesEditor.Cancel, (_, _) => Close());
        _btnSave = AddButton(buttons, Strings.MonsterSpeciesEditor.Save, SaveStatuses);
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
    private void Flag(string label, Func<MonsterStatusDescriptor, bool> get, Action<MonsterStatusDescriptor, bool> set)
    {
        var control = new DarkCheckBox();
        AddField(label, control);
        _readFields[control] = status => control.Checked = get(status);
        control.CheckedChanged += (_, _) => { if (!_loading && _selected is { } status) set(status, control.Checked); };
    }

    private DarkNumericUpDown Number(string label, decimal minimum, decimal maximum, Func<MonsterStatusDescriptor, decimal> get,
        Action<MonsterStatusDescriptor, decimal> set, int decimals = 0)
    {
        var control = new DarkNumericUpDown { Minimum = minimum, Maximum = maximum, DecimalPlaces = decimals };
        AddField(label, control);
        _readFields[control] = status => control.Value = Math.Clamp(get(status), minimum, maximum);
        control.ValueChanged += (_, _) => { if (!_loading && _selected is { } status) set(status, control.Value); };
        return control;
    }

    private DarkComboBox SelectEnum<T>(string label, Func<MonsterStatusDescriptor, T> get, Action<MonsterStatusDescriptor, T> set) where T : struct, Enum
    {
        var control = Choice();
        control.Items.AddRange(Enum.GetValues<T>().Cast<object>().ToArray());
        AddField(label, control);
        _readFields[control] = status => control.SelectedItem = get(status);
        control.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading && _selected is { } status && control.SelectedItem is T value) set(status, value);
        };
        return control;
    }

    private void Animation(string label, Func<MonsterStatusDescriptor, Guid?> get, Action<MonsterStatusDescriptor, Guid?> set)
    {
        var control = Choice();
        AddField(label, control);
        _readFields[control] = status => PopulateChoice(control, AnimationDescriptor.Lookup.Values.Cast<AnimationDescriptor>()
            .Select(animation => new ContentChoice(animation.Id, animation.Name)), get(status) ?? Guid.Empty);
        control.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading && _selected is { } status && control.SelectedItem is ContentChoice choice)
                set(status, choice.Id == Guid.Empty ? null : choice.Id);
        };
    }

    public void InitEditor()
    {
        foreach (var id in _drafts.Keys.Where(id => MonsterStatusDescriptor.Get(id) == null).ToArray())
        {
            _drafts.Remove(id);
            _originals.Remove(id);
        }
        var types = MonsterStatusDescriptor.Lookup.Values.Cast<MonsterStatusDescriptor>()
            .Select(type => _drafts.GetValueOrDefault(type.Id) ?? type).OrderBy(type => type.Name).ToArray();
        _statusList.Repopulate(types.Select(type => new KeyValuePair<Guid, KeyValuePair<string, string>>(type.Id, new(type.Name, type.Folder))).ToArray(),
            types.Select(type => type.Folder).Where(folder => !string.IsNullOrEmpty(folder)).Distinct().Order().ToList(), false, false, string.Empty);
        if (_selected != null && MonsterStatusDescriptor.Get(_selected.Id) == null) _selected = null;
        if (_selected == null && types.Length > 0)
        {
            _statusList.SelectedNode = _statusList.Nodes.Cast<TreeNode>()
                .SelectMany(node => node.Tag is Guid ? new[] { node } : node.Nodes.Cast<TreeNode>()).First();
            SelectStatus((Guid)_statusList.SelectedNode.Tag);
        }
        ShowFields();
    }
    protected override void GameObjectUpdatedDelegate(GameObjectType type)
    {
        if (type == GameObjectType.Animation) { ShowFields(); return; }
        if (type != GameObjectType.MonsterStatus) return;
        foreach (var (id, draft) in _drafts)
            if (draft.JsonData == _originals[id] && MonsterStatusDescriptor.Get(id) is { } source)
            {
                draft.Load(source.JsonData);
                draft.MakeBackup();
                _originals[id] = draft.JsonData;
            }
        InitEditor();
    }
    private void SelectStatus(Guid id)
    {
        if (!MonsterStatusDescriptor.TryGet(id, out var source)) return;
        if (!_drafts.TryGetValue(id, out var draft))
        {
            draft = new MonsterStatusDescriptor(id);
            draft.Load(source.JsonData);
            draft.MakeBackup();
            _drafts.Add(id, draft);
            _originals.Add(id, draft.JsonData);
        }
        _selected = draft;
        ShowFields();
    }
    private void PopulateChoice(DarkComboBox combo, IEnumerable<ContentChoice> choices, Guid selected)
    {
        combo.Items.Clear();
        combo.Items.Add(new ContentChoice(Guid.Empty, Strings.General.None));
        combo.Items.AddRange(choices.OrderBy(choice => choice.Name).ToArray());
        if (!combo.Items.Cast<ContentChoice>().Any(choice => choice.Id == selected))
            combo.Items.Add(new ContentChoice(selected, Strings.MonsterMoveEditor.Missing.ToString(selected)));
        combo.SelectedItem = combo.Items.Cast<ContentChoice>().First(choice => choice.Id == selected);
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
            foreach (var read in _readFields.Values) read(_selected);
        }
        finally { _loading = false; }
    }
    private void EditFields(object? sender, EventArgs e)
    {
        if (_loading || _selected == null) return;
        _selected.Name = _name.Text;
        _selected.Description = _description.Text;
        _statusList.UpdateText(_selected.Name);
    }
    private void NewStatus(object? sender, EventArgs e) => PacketSender.SendCreateObject(GameObjectType.MonsterStatus);
    private void CopyStatus(object? sender, EventArgs e) => _copied = _selected?.JsonData;
    private void PasteStatus(object? sender, EventArgs e)
    {
        if (_selected == null || _copied == null) return;
        _selected.Load(_copied, keepCreationTime: true);
        InitEditor();
    }
    private void UndoStatus(object? sender, EventArgs e) { _selected?.RestoreBackup(); InitEditor(); }
    private void DeleteStatus(object? sender, EventArgs e)
    {
        if (_selected != null && DarkMessageBox.ShowWarning("Delete this monster status?", Text,
                DarkDialogButton.YesNo, Icon) == DialogResult.Yes) PacketSender.SendDeleteObject(_selected);
    }
    private void SaveStatuses(object? sender, EventArgs e)
    {
        var changed = _drafts.Values.Where(type => type.JsonData != _originals[type.Id]).ToArray();
        if (changed.Any(status => !status.IsValid || status.AnimationReferences().Any(id => AnimationDescriptor.Get(id) == null)))
        {
            DarkMessageBox.ShowWarning("Check the name, turn range, damage amount and animation references. Fixed damage uses whole HP and percentage damage must be greater than zero and at most 100.", Text);
            return;
        }
        foreach (var type in changed) PacketSender.SendSaveObject(type);
        Close();
    }
}
