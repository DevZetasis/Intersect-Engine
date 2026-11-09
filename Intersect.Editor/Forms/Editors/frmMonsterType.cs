using DarkUI.Controls;
using DarkUI.Forms;
using Intersect.Editor.Forms.Controls;
using Intersect.Editor.General;
using Intersect.Editor.Localization;
using Intersect.Editor.Networking;
using Intersect.Enums;
using Intersect.Framework.Core.GameObjects.Monsters;

namespace Intersect.Editor.Forms.Editors;

public sealed class FrmMonsterType : EditorForm
{
    private readonly GameObjectList _typeList = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<Guid, MonsterTypeDescriptor> _drafts = new();
    private readonly Dictionary<Guid, string> _originals = new();
    private readonly DarkTextBox _id = new() { ReadOnly = true };
    private readonly DarkTextBox _name = new();
    private readonly DarkTextBox _description = new() { Multiline = true, Height = 80, ScrollBars = ScrollBars.Vertical };
    private readonly DarkTextBox _displayColor = new();
    private readonly TableLayoutPanel _fields = new() { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), AutoScroll = true };
    private readonly TableLayoutPanel _effectiveness = new() { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top };
    private readonly Dictionary<Guid, DarkComboBox> _multipliers = new();
    private static readonly double[] MultiplierValues = [0, 0.5, 1, 2];
    private MonsterTypeDescriptor? _selected;
    private string? _copied;
    private bool _loading;

    public FrmMonsterType()
    {
        Text = Strings.MonsterTypeEditor.Menu;
        Size = new Size(880, 680);
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
        ForeColor = System.Drawing.Color.Gainsboro;
        _typeList.BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
        _typeList.ForeColor = ForeColor;
        _typeList.Init(() => { }, SelectType, NewType, CopyType, UndoType, PasteType, DeleteType);
        var listPanel = new Panel { Dock = DockStyle.Left, Width = 245, Padding = new Padding(8) };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        AddButton(tools, Strings.MonsterSpeciesEditor.New, NewType);
        AddButton(tools, Strings.MonsterSpeciesEditor.Copy, CopyType);
        AddButton(tools, Strings.MonsterSpeciesEditor.Paste, PasteType);
        AddButton(tools, Strings.MonsterSpeciesEditor.Undo, UndoType);
        AddButton(tools, Strings.MonsterSpeciesEditor.Delete, DeleteType);
        listPanel.Controls.Add(_typeList);
        listPanel.Controls.Add(tools);
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(Strings.MonsterTypeEditor.Id, _id);
        AddField(Strings.MonsterTypeEditor.Name, _name);
        AddField(Strings.MonsterTypeEditor.Description, _description);
        var colorPanel = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top };
        colorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        colorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        _displayColor.Dock = DockStyle.Top;
        colorPanel.Controls.Add(_displayColor, 0, 0);
        var choose = AddButton(colorPanel, Strings.MonsterTypeEditor.ChooseColor, ChooseColor);
        colorPanel.SetCellPosition(choose, new TableLayoutPanelCellPosition(1, 0));
        AddField(Strings.MonsterTypeEditor.Color, colorPanel);
        AddField(string.Empty, new DarkLabel { Text = Strings.MonsterTypeEditor.Direction, Height = 55, AutoSize = false });
        _effectiveness.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        _effectiveness.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        AddField(Strings.MonsterTypeEditor.Effectiveness, _effectiveness);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5) };
        _btnCancel = AddButton(buttons, Strings.MonsterSpeciesEditor.Cancel, (_, _) => Close());
        _btnSave = AddButton(buttons, Strings.MonsterSpeciesEditor.Save, SaveTypes);
        Controls.Add(_fields);
        Controls.Add(listPanel);
        Controls.Add(buttons);
        _name.TextChanged += EditFields;
        _description.TextChanged += EditFields;
        _displayColor.TextChanged += EditFields;
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
        foreach (var id in _drafts.Keys.Where(id => MonsterTypeDescriptor.Get(id) == null).ToArray())
        {
            _drafts.Remove(id);
            _originals.Remove(id);
        }
        var types = MonsterTypeDescriptor.Lookup.Values.Cast<MonsterTypeDescriptor>()
            .Select(type => _drafts.GetValueOrDefault(type.Id) ?? type).OrderBy(type => type.Name).ToArray();
        _typeList.Repopulate(types.Select(type => new KeyValuePair<Guid, KeyValuePair<string, string>>(type.Id, new(type.Name, type.Folder))).ToArray(),
            types.Select(type => type.Folder).Where(folder => !string.IsNullOrEmpty(folder)).Distinct().Order().ToList(), false, false, string.Empty);
        if (_selected != null && MonsterTypeDescriptor.Get(_selected.Id) == null) _selected = null;
        if (_selected == null && types.Length > 0)
        {
            _typeList.SelectedNode = _typeList.Nodes.Cast<TreeNode>()
                .SelectMany(node => node.Tag is Guid ? new[] { node } : node.Nodes.Cast<TreeNode>()).First();
            SelectType((Guid)_typeList.SelectedNode.Tag);
        }
        ShowFields();
    }
    protected override void GameObjectUpdatedDelegate(GameObjectType type)
    {
        if (type != GameObjectType.MonsterType) return;
        foreach (var (id, draft) in _drafts)
            if (draft.JsonData == _originals[id] && MonsterTypeDescriptor.Get(id) is { } source)
            {
                draft.Load(source.JsonData);
                draft.MakeBackup();
                _originals[id] = draft.JsonData;
            }
        InitEditor();
    }
    private void SelectType(Guid id)
    {
        if (!MonsterTypeDescriptor.TryGet(id, out var source)) return;
        if (!_drafts.TryGetValue(id, out var draft))
        {
            draft = new MonsterTypeDescriptor(id);
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
            _displayColor.Text = _selected.DisplayColor;
            foreach (var control in _effectiveness.Controls.Cast<Control>().ToArray()) control.Dispose();
            _effectiveness.Controls.Clear();
            _effectiveness.RowStyles.Clear();
            _effectiveness.RowCount = 0;
            _multipliers.Clear();
            var targets = MonsterTypeDescriptor.Lookup.Values.Cast<MonsterTypeDescriptor>().Select(type => (type.Id, type.Name))
                .Concat(_selected.Effectiveness.Keys.Where(id => MonsterTypeDescriptor.Get(id) == null)
                    .Select(id => (id, Strings.MonsterTypeEditor.Missing.ToString(id)))).OrderBy(type => type.Item2);
            foreach (var (id, name) in targets)
            {
                var row = _effectiveness.RowCount++;
                _effectiveness.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _effectiveness.Controls.Add(new DarkLabel { Text = name, AutoSize = true, Margin = new Padding(0, 7, 8, 7) }, 0, row);
                var choice = new DarkComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Tag = id };
                choice.Items.AddRange(new object[] { Strings.MonsterTypeEditor.Immune.ToString(), Strings.MonsterTypeEditor.Resistant.ToString(),
                    Strings.MonsterTypeEditor.Neutral.ToString(), Strings.MonsterTypeEditor.Weak.ToString() });
                choice.SelectedIndex = Array.IndexOf(MultiplierValues, _selected.Effectiveness.GetValueOrDefault(id, 1));
                choice.SelectedIndexChanged += (_, _) =>
                {
                    if (_loading || _selected == null || choice.SelectedIndex < 0) return;
                    var value = MultiplierValues[choice.SelectedIndex];
                    if (value == 1) _selected.Effectiveness.Remove(id);
                    else _selected.Effectiveness[id] = value;
                };
                _effectiveness.Controls.Add(choice, 1, row);
                _multipliers.Add(id, choice);
            }
        }
        finally { _loading = false; }
    }
    private void EditFields(object? sender, EventArgs e)
    {
        if (_loading || _selected == null) return;
        _selected.Name = _name.Text;
        _selected.Description = _description.Text;
        _selected.DisplayColor = _displayColor.Text.Trim();
        _typeList.UpdateText(_selected.Name);
    }
    private void ChooseColor(object? sender, EventArgs e)
    {
        using var dialog = new ColorDialog { FullOpen = true };
        if (_selected?.IsValid == true) dialog.Color = System.Drawing.ColorTranslator.FromHtml(_selected.DisplayColor);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _displayColor.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
    }
    private void NewType(object? sender, EventArgs e) => PacketSender.SendCreateObject(GameObjectType.MonsterType);
    private void CopyType(object? sender, EventArgs e) => _copied = _selected?.JsonData;
    private void PasteType(object? sender, EventArgs e)
    {
        if (_selected == null || _copied == null) return;
        _selected.Load(_copied, keepCreationTime: true);
        InitEditor();
    }
    private void UndoType(object? sender, EventArgs e) { _selected?.RestoreBackup(); InitEditor(); }
    private void DeleteType(object? sender, EventArgs e)
    {
        if (_selected != null && DarkMessageBox.ShowWarning(Strings.MonsterTypeEditor.DeletePrompt, Text,
                DarkDialogButton.YesNo, Icon) == DialogResult.Yes) PacketSender.SendDeleteObject(_selected);
    }
    private void SaveTypes(object? sender, EventArgs e)
    {
        var changed = _drafts.Values.Where(type => type.JsonData != _originals[type.Id]).ToArray();
        if (changed.Any(type => !type.IsValid || type.Effectiveness.Keys.Any(id => MonsterTypeDescriptor.Get(id) == null)))
        {
            DarkMessageBox.ShowWarning(Strings.MonsterTypeEditor.Invalid, Text);
            return;
        }
        foreach (var type in changed) PacketSender.SendSaveObject(type);
        Close();
    }
}
