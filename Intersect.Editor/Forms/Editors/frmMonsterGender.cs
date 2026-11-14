using DarkUI.Controls;
using DarkUI.Forms;
using Intersect.Editor.Forms.Controls;
using Intersect.Editor.General;
using Intersect.Editor.Localization;
using Intersect.Editor.Networking;
using Intersect.Enums;
using Intersect.Framework.Core.GameObjects.Monsters;

namespace Intersect.Editor.Forms.Editors;

public sealed class FrmMonsterGender : EditorForm
{
    private readonly GameObjectList _genderList = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<Guid, MonsterGenderDescriptor> _drafts = new();
    private readonly Dictionary<Guid, string> _originals = new();
    private readonly DarkTextBox _id = new() { ReadOnly = true };
    private readonly DarkTextBox _name = new();
    private readonly DarkTextBox _description = new() { Multiline = true, Height = 80, ScrollBars = ScrollBars.Vertical };
    private readonly Dictionary<Control, Action<MonsterGenderDescriptor>> _readFields = new();
    private readonly TableLayoutPanel _fields = new() { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), AutoScroll = true };
    private MonsterGenderDescriptor? _selected;
    private string? _copied;
    private bool _loading;

    public FrmMonsterGender()
    {
        Text = "Monster Gender Editor";
        Size = new Size(840, 700);
        MinimumSize = new Size(760, 580);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
        ForeColor = System.Drawing.Color.Gainsboro;
        _genderList.BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
        _genderList.ForeColor = ForeColor;
        _genderList.Init(() => { }, SelectGender, NewGender, CopyGender, UndoGender, PasteGender, DeleteGender);
        var listPanel = new Panel { Dock = DockStyle.Left, Width = 245, Padding = new Padding(8) };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        AddButton(tools, Strings.MonsterSpeciesEditor.New, NewGender);
        AddButton(tools, Strings.MonsterSpeciesEditor.Copy, CopyGender);
        AddButton(tools, Strings.MonsterSpeciesEditor.Paste, PasteGender);
        AddButton(tools, Strings.MonsterSpeciesEditor.Undo, UndoGender);
        AddButton(tools, Strings.MonsterSpeciesEditor.Delete, DeleteGender);
        listPanel.Controls.Add(_genderList);
        listPanel.Controls.Add(tools);
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(Strings.MonsterMoveEditor.Id, _id);
        AddField(Strings.MonsterMoveEditor.Name, _name);
        AddField(Strings.MonsterMoveEditor.Description, _description);
        var symbol = new DarkTextBox { MaxLength = 16 };
        AddField("Symbol / short label", symbol);
        _readFields[symbol] = gender => symbol.Text = gender.Symbol;
        symbol.TextChanged += (_, _) => { if (!_loading && _selected is { } gender) gender.Symbol = symbol.Text; };
        var folder = new DarkTextBox();
        AddField("Folder", folder);
        _readFields[folder] = status => folder.Text = status.Folder;
        folder.TextChanged += (_, _) => { if (!_loading && _selected is { } status) status.Folder = folder.Text.Trim(); };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5) };
        _btnCancel = AddButton(buttons, Strings.MonsterSpeciesEditor.Cancel, (_, _) => Close());
        _btnSave = AddButton(buttons, Strings.MonsterSpeciesEditor.Save, SaveGenders);
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
        foreach (var id in _drafts.Keys.Where(id => MonsterGenderDescriptor.Get(id) == null).ToArray())
        {
            _drafts.Remove(id);
            _originals.Remove(id);
        }
        var types = MonsterGenderDescriptor.Lookup.Values.Cast<MonsterGenderDescriptor>()
            .Select(type => _drafts.GetValueOrDefault(type.Id) ?? type).OrderBy(type => type.Name).ToArray();
        _genderList.Repopulate(types.Select(type => new KeyValuePair<Guid, KeyValuePair<string, string>>(type.Id, new(type.Name, type.Folder))).ToArray(),
            types.Select(type => type.Folder).Where(folder => !string.IsNullOrEmpty(folder)).Distinct().Order().ToList(), false, false, string.Empty);
        if (_selected != null && MonsterGenderDescriptor.Get(_selected.Id) == null) _selected = null;
        if (_selected == null && types.Length > 0)
        {
            _genderList.SelectedNode = _genderList.Nodes.Cast<TreeNode>()
                .SelectMany(node => node.Tag is Guid ? new[] { node } : node.Nodes.Cast<TreeNode>()).First();
            SelectGender((Guid)_genderList.SelectedNode.Tag);
        }
        ShowFields();
    }
    protected override void GameObjectUpdatedDelegate(GameObjectType type)
    {
        if (type != GameObjectType.MonsterGender) return;
        foreach (var (id, draft) in _drafts)
            if (draft.JsonData == _originals[id] && MonsterGenderDescriptor.Get(id) is { } source)
            {
                draft.Load(source.JsonData);
                draft.MakeBackup();
                _originals[id] = draft.JsonData;
            }
        InitEditor();
    }
    private void SelectGender(Guid id)
    {
        if (!MonsterGenderDescriptor.TryGet(id, out var source)) return;
        if (!_drafts.TryGetValue(id, out var draft))
        {
            draft = new MonsterGenderDescriptor(id);
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
            foreach (var read in _readFields.Values) read(_selected);
        }
        finally { _loading = false; }
    }
    private void EditFields(object? sender, EventArgs e)
    {
        if (_loading || _selected == null) return;
        _selected.Name = _name.Text;
        _selected.Description = _description.Text;
        _genderList.UpdateText(_selected.Name);
    }
    private void NewGender(object? sender, EventArgs e) => PacketSender.SendCreateObject(GameObjectType.MonsterGender);
    private void CopyGender(object? sender, EventArgs e) => _copied = _selected?.JsonData;
    private void PasteGender(object? sender, EventArgs e)
    {
        if (_selected == null || _copied == null) return;
        _selected.Load(_copied, keepCreationTime: true);
        InitEditor();
    }
    private void UndoGender(object? sender, EventArgs e) { _selected?.RestoreBackup(); InitEditor(); }
    private void DeleteGender(object? sender, EventArgs e)
    {
        if (_selected != null && DarkMessageBox.ShowWarning("Delete this monster gender?", Text,
                DarkDialogButton.YesNo, Icon) == DialogResult.Yes) PacketSender.SendDeleteObject(_selected);
    }
    private void SaveGenders(object? sender, EventArgs e)
    {
        var changed = _drafts.Values.Where(type => type.JsonData != _originals[type.Id]).ToArray();
        if (changed.Any(status => !status.IsValid))
        {
            DarkMessageBox.ShowWarning("Enter a name of 1–80 characters and an optional symbol of at most 16 characters.", Text);
            return;
        }
        foreach (var type in changed) PacketSender.SendSaveObject(type);
        Close();
    }
}
