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

public sealed class FrmMonsterMove : EditorForm
{
    private readonly GameObjectList _moveList = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<Guid, MonsterMoveDescriptor> _drafts = new();
    private readonly Dictionary<Guid, string> _originals = new();
    private readonly DarkTextBox _id = new() { ReadOnly = true };
    private readonly DarkTextBox _name = new();
    private readonly DarkTextBox _description = new() { Multiline = true, Height = 80, ScrollBars = ScrollBars.Vertical };
    private readonly DarkComboBox _type = Choice();
    private readonly DarkComboBox _category = Choice();
    private readonly DarkComboBox _target = Choice();
    private readonly DarkComboBox _animation = Choice();
    private readonly DarkComboBox _animationPosition = Choice();
    private readonly MonsterMoveEffectsControl _effects = new();
    private readonly DarkNumericUpDown _power = new() { Minimum = 0, Maximum = int.MaxValue, Value = 1 };
    private readonly DarkNumericUpDown _pp = new() { Minimum = 1, Maximum = 999, Value = 20 };
    private readonly DarkNumericUpDown _accuracy = new() { Minimum = 1, Maximum = 100, Value = 100 };
    private readonly DarkCheckBox _alwaysHits = new() { Text = Strings.MonsterMoveEditor.AlwaysHits, AutoSize = true };
    private readonly DarkNumericUpDown _priority = new() { Minimum = int.MinValue, Maximum = int.MaxValue };
    private readonly TableLayoutPanel _fields = new() { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), AutoScroll = true };
    private sealed record ContentChoice(Guid Id, string Name) { public override string ToString() => Name; }
    private MonsterMoveDescriptor? _selected;
    private string? _copied;
    private bool _loading;
    private static DarkComboBox Choice() => new() { DropDownStyle = ComboBoxStyle.DropDownList };

    public FrmMonsterMove()
    {
        Text = Strings.MonsterMoveEditor.Menu;
        Size = new Size(840, 700);
        MinimumSize = new Size(760, 580);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
        ForeColor = System.Drawing.Color.Gainsboro;
        _moveList.BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
        _moveList.ForeColor = ForeColor;
        _moveList.Init(() => { }, SelectMove, NewMove, CopyMove, UndoMove, PasteMove, DeleteMove);
        var listPanel = new Panel { Dock = DockStyle.Left, Width = 245, Padding = new Padding(8) };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        AddButton(tools, Strings.MonsterSpeciesEditor.New, NewMove);
        AddButton(tools, Strings.MonsterSpeciesEditor.Copy, CopyMove);
        AddButton(tools, Strings.MonsterSpeciesEditor.Paste, PasteMove);
        AddButton(tools, Strings.MonsterSpeciesEditor.Undo, UndoMove);
        AddButton(tools, Strings.MonsterSpeciesEditor.Delete, DeleteMove);
        listPanel.Controls.Add(_moveList);
        listPanel.Controls.Add(tools);
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(Strings.MonsterMoveEditor.Id, _id);
        AddField(Strings.MonsterMoveEditor.Name, _name);
        AddField(Strings.MonsterMoveEditor.Description, _description);
        AddField(Strings.MonsterMoveEditor.Type, _type);
        AddField(Strings.MonsterMoveEditor.Category, _category);
        AddField(Strings.MonsterMoveEditor.Power, _power);
        AddField(Strings.MonsterMoveEditor.MaximumPP, _pp);
        AddField(Strings.MonsterMoveEditor.Accuracy, _accuracy);
        AddField(string.Empty, _alwaysHits);
        AddField(Strings.MonsterMoveEditor.Priority, _priority);
        AddField(Strings.MonsterMoveEditor.Target, _target);
        AddField(Strings.MonsterMoveEditor.Animation, _animation);
        AddField(Strings.MonsterMoveEditor.AnimationPosition, _animationPosition);
        AddField(Strings.MonsterMoveEditor.Effects, _effects);
        _animationPosition.Items.AddRange(new object[] { Strings.MonsterMoveEditor.AnimationTarget.ToString(),
            Strings.MonsterMoveEditor.AnimationAttacker.ToString(), Strings.MonsterMoveEditor.AnimationBoth.ToString() });
        _category.Items.AddRange(new object[] { Strings.MonsterMoveEditor.Physical.ToString(), Strings.MonsterMoveEditor.Special.ToString(), Strings.MonsterMoveEditor.Status.ToString() });
        _target.Items.AddRange(new object[] { Strings.MonsterMoveEditor.OneOpponent.ToString(), Strings.MonsterMoveEditor.Self.ToString(),
            Strings.MonsterMoveEditor.OneAlly.ToString(), Strings.MonsterMoveEditor.AllOpponents.ToString(), Strings.MonsterMoveEditor.AllBattlefield.ToString() });
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5) };
        _btnCancel = AddButton(buttons, Strings.MonsterSpeciesEditor.Cancel, (_, _) => Close());
        _btnSave = AddButton(buttons, Strings.MonsterSpeciesEditor.Save, SaveMoves);
        Controls.Add(_fields);
        Controls.Add(listPanel);
        Controls.Add(buttons);
        _name.TextChanged += EditFields;
        _description.TextChanged += EditFields;
        foreach (var choice in new[] { _type, _category, _target, _animation, _animationPosition }) choice.SelectedIndexChanged += EditFields;
        foreach (var number in new[] { _power, _accuracy, _priority, _pp }) number.ValueChanged += EditFields;
        _alwaysHits.CheckedChanged += EditFields;
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
        foreach (var id in _drafts.Keys.Where(id => MonsterMoveDescriptor.Get(id) == null).ToArray())
        {
            _drafts.Remove(id);
            _originals.Remove(id);
        }
        var types = MonsterMoveDescriptor.Lookup.Values.Cast<MonsterMoveDescriptor>()
            .Select(type => _drafts.GetValueOrDefault(type.Id) ?? type).OrderBy(type => type.Name).ToArray();
        _moveList.Repopulate(types.Select(type => new KeyValuePair<Guid, KeyValuePair<string, string>>(type.Id, new(type.Name, type.Folder))).ToArray(),
            types.Select(type => type.Folder).Where(folder => !string.IsNullOrEmpty(folder)).Distinct().Order().ToList(), false, false, string.Empty);
        if (_selected != null && MonsterMoveDescriptor.Get(_selected.Id) == null) _selected = null;
        if (_selected == null && types.Length > 0)
        {
            _moveList.SelectedNode = _moveList.Nodes.Cast<TreeNode>()
                .SelectMany(node => node.Tag is Guid ? new[] { node } : node.Nodes.Cast<TreeNode>()).First();
            SelectMove((Guid)_moveList.SelectedNode.Tag);
        }
        ShowFields();
    }
    protected override void GameObjectUpdatedDelegate(GameObjectType type)
    {
        if (type is GameObjectType.MonsterType or GameObjectType.Animation or GameObjectType.MonsterStatus) { ShowFields(); return; }
        if (type != GameObjectType.MonsterMove) return;
        foreach (var (id, draft) in _drafts)
            if (draft.JsonData == _originals[id] && MonsterMoveDescriptor.Get(id) is { } source)
            {
                draft.Load(source.JsonData);
                draft.MakeBackup();
                _originals[id] = draft.JsonData;
            }
        InitEditor();
    }
    private void SelectMove(Guid id)
    {
        if (!MonsterMoveDescriptor.TryGet(id, out var source)) return;
        if (!_drafts.TryGetValue(id, out var draft))
        {
            draft = new MonsterMoveDescriptor(id);
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
        _effects.Bind(_selected);
        if (_selected == null) return;
        _loading = true;
        try
        {
            _id.Text = _selected.Id.ToString();
            _name.Text = _selected.Name;
            _description.Text = _selected.Description;
            PopulateChoice(_type, MonsterTypeDescriptor.Lookup.Values.Cast<MonsterTypeDescriptor>().Select(type => new ContentChoice(type.Id, type.Name)), _selected.TypeId);
            PopulateChoice(_animation, AnimationDescriptor.Lookup.Values.Cast<AnimationDescriptor>().Select(animation => new ContentChoice(animation.Id, animation.Name)), _selected.AnimationId ?? Guid.Empty);
            _animationPosition.SelectedIndex = Enum.IsDefined(_selected.AnimationPosition) ? (int)_selected.AnimationPosition : 0;
            _category.SelectedIndex = (int)_selected.Category;
            _power.Minimum = _selected.Category == MonsterMoveCategory.Status ? 0 : 1;
            _power.Value = _selected.BasePower;
            _pp.Value = _selected.MaximumPP;
            _accuracy.Value = _selected.Accuracy;
            _alwaysHits.Checked = _selected.AlwaysHits;
            _accuracy.Enabled = !_selected.AlwaysHits;
            _priority.Value = _selected.Priority;
            _target.SelectedIndex = (int)_selected.Target;
        }
        finally { _loading = false; }
    }
    private void EditFields(object? sender, EventArgs e)
    {
        if (_loading || _selected == null) return;
        _selected.Name = _name.Text;
        _selected.Description = _description.Text;
        _selected.TypeId = (_type.SelectedItem as ContentChoice)?.Id ?? Guid.Empty;
        _selected.Category = (MonsterMoveCategory)_category.SelectedIndex;
        _loading = true;
        try { _power.Minimum = _selected.Category == MonsterMoveCategory.Status ? 0 : 1; }
        finally { _loading = false; }
        _selected.BasePower = (int)_power.Value;
        _selected.MaximumPP = (int)_pp.Value;
        _selected.Accuracy = (int)_accuracy.Value;
        _selected.AlwaysHits = _alwaysHits.Checked;
        _accuracy.Enabled = !_alwaysHits.Checked;
        _selected.Priority = (int)_priority.Value;
        _selected.Target = (MonsterMoveTarget)_target.SelectedIndex;
        var animation = (_animation.SelectedItem as ContentChoice)?.Id ?? Guid.Empty;
        _selected.AnimationId = animation == Guid.Empty ? null : animation;
        _selected.AnimationPosition = (BattleMoveAnimationPosition)_animationPosition.SelectedIndex;
        _moveList.UpdateText(_selected.Name);
    }
    private void NewMove(object? sender, EventArgs e) => PacketSender.SendCreateObject(GameObjectType.MonsterMove);
    private void CopyMove(object? sender, EventArgs e) => _copied = _selected?.JsonData;
    private void PasteMove(object? sender, EventArgs e)
    {
        if (_selected == null || _copied == null) return;
        _selected.Load(_copied, keepCreationTime: true);
        InitEditor();
    }
    private void UndoMove(object? sender, EventArgs e) { _selected?.RestoreBackup(); InitEditor(); }
    private void DeleteMove(object? sender, EventArgs e)
    {
        if (_selected != null && DarkMessageBox.ShowWarning(Strings.MonsterMoveEditor.DeletePrompt, Text,
                DarkDialogButton.YesNo, Icon) == DialogResult.Yes) PacketSender.SendDeleteObject(_selected);
    }
    private void SaveMoves(object? sender, EventArgs e)
    {
        var changed = _drafts.Values.Where(type => type.JsonData != _originals[type.Id]).ToArray();
        if (changed.Any(move => !move.HasValidEffects || move.Effects.Any(effect => effect.StatusId is { } id && MonsterStatusDescriptor.Get(id) == null)))
        {
            DarkMessageBox.ShowWarning(Strings.MonsterMoveEditor.InvalidEffects, Text);
            return;
        }
        if (changed.Any(type => !type.IsValid || MonsterTypeDescriptor.Get(type.TypeId) == null ||
            (type.AnimationId is { } animation && AnimationDescriptor.Get(animation) == null)))
        {
            DarkMessageBox.ShowWarning(Strings.MonsterMoveEditor.Invalid, Text);
            return;
        }
        foreach (var type in changed) PacketSender.SendSaveObject(type);
        Close();
    }
}
