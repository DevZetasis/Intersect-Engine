using DarkUI.Controls;
using DarkUI.Forms;
using Intersect.Editor.Content;
using Intersect.Editor.Forms.Controls;
using Intersect.Editor.General;
using Intersect.Editor.Localization;
using Intersect.Editor.Networking;
using Intersect.Enums;
using Intersect.Framework.Core.GameObjects.Monsters;

namespace Intersect.Editor.Forms.Editors;

public sealed class FrmMonsterSpecies : EditorForm
{
    private readonly GameObjectList _speciesList = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<Guid, MonsterSpeciesDescriptor> _drafts = new();
    private readonly Dictionary<Guid, string> _originals = new();
    private readonly DarkTextBox _name = new();
    private readonly DarkTextBox _description = new() { Multiline = true, Height = 80, ScrollBars = ScrollBars.Vertical };
    private readonly DarkTextBox _folder = new();
    private readonly DarkTextBox _id = new() { ReadOnly = true };
    private readonly DarkComboBox _sprite = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DarkComboBox _frontBattleSprite = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DarkComboBox _backBattleSprite = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DarkComboBox _battleBackground = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DarkNumericUpDown _hp = StatInput();
    private readonly DarkNumericUpDown _attack = StatInput();
    private readonly DarkNumericUpDown _defense = StatInput();
    private readonly DarkNumericUpDown _specialAttack = StatInput();
    private readonly DarkNumericUpDown _specialDefense = StatInput();
    private readonly DarkNumericUpDown _speed = StatInput();
    private readonly MonsterLearnsetEditor _learnset = new();
    private readonly DarkComboBox _primaryType = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DarkComboBox _secondaryType = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private sealed record TypeChoice(Guid Id, string Name) { public override string ToString() => Name; }
    private readonly PictureBox _preview = new() { Size = new Size(96, 96), BackColor = System.Drawing.Color.Black };
    private readonly TableLayoutPanel _fields = new() { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), AutoScroll = true };
    private MonsterSpeciesDescriptor? _selected;
    private string? _copied;
    private bool _loading;

    public FrmMonsterSpecies()
    {
        Text = Strings.MonsterSpeciesEditor.Menu;
        Size = new Size(800, 730);
        MinimumSize = new Size(720, 580);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
        ForeColor = System.Drawing.Color.Gainsboro;
        _speciesList.BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
        _speciesList.ForeColor = ForeColor;
        _speciesList.Init(() => { }, SelectSpecies, NewSpecies, CopySpecies, UndoSpecies, PasteSpecies, DeleteSpecies);

        var listPanel = new Panel { Dock = DockStyle.Left, Width = 245, Padding = new Padding(8) };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        AddButton(tools, Strings.MonsterSpeciesEditor.New, NewSpecies);
        AddButton(tools, Strings.MonsterSpeciesEditor.Copy, CopySpecies);
        AddButton(tools, Strings.MonsterSpeciesEditor.Paste, PasteSpecies);
        AddButton(tools, Strings.MonsterSpeciesEditor.Undo, UndoSpecies);
        AddButton(tools, Strings.MonsterSpeciesEditor.Delete, DeleteSpecies);
        listPanel.Controls.Add(_speciesList);
        listPanel.Controls.Add(tools);

        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(Strings.MonsterSpeciesEditor.Id, _id);
        AddField(Strings.MonsterSpeciesEditor.Name, _name);
        AddField(Strings.MonsterSpeciesEditor.Description, _description);
        AddField(Strings.MonsterSpeciesEditor.Folder, _folder);
        AddField(Strings.MonsterSpeciesEditor.Sprite, _sprite);
        AddField(string.Empty, _preview);
        AddField(Strings.MonsterSpeciesEditor.FrontBattleSprite, _frontBattleSprite);
        AddField(Strings.MonsterSpeciesEditor.BackBattleSprite, _backBattleSprite);
        AddField(Strings.MonsterSpeciesEditor.BattleBackground, _battleBackground);
        AddField(Strings.MonsterSpeciesEditor.HP, _hp);
        AddField(Strings.MonsterSpeciesEditor.Attack, _attack);
        AddField(Strings.MonsterSpeciesEditor.Defense, _defense);
        AddField(Strings.MonsterSpeciesEditor.SpecialAttack, _specialAttack);
        AddField(Strings.MonsterSpeciesEditor.SpecialDefense, _specialDefense);
        AddField(Strings.MonsterSpeciesEditor.Speed, _speed);
        AddField(Strings.MonsterSpeciesEditor.PrimaryType, _primaryType);
        AddField(Strings.MonsterSpeciesEditor.SecondaryType, _secondaryType);
        AddField(Strings.MonsterMoveEditor.Learnset, _learnset);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5) };
        _btnCancel = AddButton(buttons, Strings.MonsterSpeciesEditor.Cancel, (_, _) => Close());
        _btnSave = AddButton(buttons, Strings.MonsterSpeciesEditor.Save, SaveSpecies);
        Controls.Add(_fields);
        Controls.Add(listPanel);
        Controls.Add(buttons);

        _sprite.Items.Add(Strings.General.None.ToString());
        _sprite.Items.AddRange(GameContentManager.GetSmartSortedTextureNames(GameContentManager.TextureType.Entity));
        foreach (var combo in new[] { _frontBattleSprite, _backBattleSprite, _battleBackground })
        {
            combo.Items.Add(Strings.General.None.ToString());
            combo.Items.AddRange(GameContentManager.GetSmartSortedTextureNames(combo == _battleBackground ? GameContentManager.TextureType.Image : GameContentManager.TextureType.Entity));
            combo.SelectedIndexChanged += EditFields;
        }
        _name.TextChanged += EditFields;
        _description.TextChanged += EditFields;
        _folder.TextChanged += EditFields;
        _sprite.SelectedIndexChanged += EditFields;
        _primaryType.SelectedIndexChanged += EditFields;
        _secondaryType.SelectedIndexChanged += EditFields;
        foreach (var stat in new[] { _hp, _attack, _defense, _specialAttack, _specialDefense, _speed }) stat.ValueChanged += EditFields;
        FormClosed += (_, _) => Globals.CurrentEditor = -1;
    }

    private static DarkNumericUpDown StatInput() => new() { Minimum = 1, Maximum = int.MaxValue, Value = 1 };

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
        control.Margin = new Padding(0, 4, 0, 4);
        if (control != _preview) control.Dock = DockStyle.Top;
        _fields.Controls.Add(control, 1, row);
    }

    public void InitEditor()
    {
        foreach (var id in _drafts.Keys.Where(id => MonsterSpeciesDescriptor.Get(id) == null).ToArray())
        {
            _drafts.Remove(id);
            _originals.Remove(id);
        }
        var species = MonsterSpeciesDescriptor.Lookup.Values.Cast<MonsterSpeciesDescriptor>()
            .Select(item => _drafts.GetValueOrDefault(item.Id) ?? item).OrderBy(item => item.Name).ToArray();
        var folders = species.Select(item => item.Folder).Where(folder => !string.IsNullOrEmpty(folder)).Distinct().Order().ToList();
        _speciesList.Repopulate(species.Select(item => new KeyValuePair<Guid, KeyValuePair<string, string>>(
            item.Id, new(item.Name, item.Folder))).ToArray(), folders, false, false, string.Empty);
        if (_selected != null && MonsterSpeciesDescriptor.Get(_selected.Id) == null) _selected = null;
        if (_selected == null && species.Length > 0)
        {
            _speciesList.SelectedNode = _speciesList.Nodes.Cast<TreeNode>()
                .SelectMany(node => node.Tag is Guid ? new[] { node } : node.Nodes.Cast<TreeNode>()).First();
            SelectSpecies((Guid)_speciesList.SelectedNode.Tag);
        }
        ShowFields();
    }

    protected override void GameObjectUpdatedDelegate(GameObjectType type)
    {
        if (type is GameObjectType.MonsterType or GameObjectType.MonsterMove) { ShowFields(); return; }
        if (type != GameObjectType.MonsterSpecies) return;
        foreach (var (id, draft) in _drafts)
        {
            // Refresh clean drafts after another editor saves; keep local unsaved work.
            if (draft.JsonData == _originals[id] && MonsterSpeciesDescriptor.Get(id) is { } source)
            {
                draft.Load(source.JsonData);
                draft.MakeBackup();
                _originals[id] = draft.JsonData;
            }
        }
        InitEditor();
    }

    private void SelectSpecies(Guid id)
    {
        if (!MonsterSpeciesDescriptor.TryGet(id, out var source)) return;
        if (!_drafts.TryGetValue(id, out var draft))
        {
            draft = new MonsterSpeciesDescriptor(id);
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
            var sprite = string.IsNullOrEmpty(_selected.Sprite) ? Strings.General.None.ToString() : _selected.Sprite;
            if (!_sprite.Items.Contains(sprite)) _sprite.Items.Add(sprite);
            _sprite.SelectedItem = sprite;
            foreach (var (combo, value) in new[] { (_frontBattleSprite, _selected.FrontBattleSprite), (_backBattleSprite, _selected.BackBattleSprite), (_battleBackground, _selected.BattleBackground) })
            {
                var choice = string.IsNullOrWhiteSpace(value) ? Strings.General.None.ToString() : value;
                if (!combo.Items.Contains(choice)) combo.Items.Add(choice);
                combo.SelectedItem = choice;
            }
            _hp.Value = _selected.BaseStats.HP;
            _attack.Value = _selected.BaseStats.Attack;
            _defense.Value = _selected.BaseStats.Defense;
            _specialAttack.Value = _selected.BaseStats.SpecialAttack;
            _specialDefense.Value = _selected.BaseStats.SpecialDefense;
            _speed.Value = _selected.BaseStats.Speed;
            LoadTypeChoices();
            _learnset.Bind(_selected.Learnset);
            DrawSprite();
        }
        finally { _loading = false; }
    }

    private void LoadTypeChoices()
    {
        if (_selected == null) return;
        var types = MonsterTypeDescriptor.Lookup.Values.Cast<MonsterTypeDescriptor>()
            .OrderBy(type => type.Name).Select(type => new TypeChoice(type.Id, type.Name)).ToArray();
        var ids = _selected.TypeIds;
        foreach (var (combo, index) in new[] { (_primaryType, 0), (_secondaryType, 1) })
        {
            combo.Items.Clear();
            combo.Items.Add(new TypeChoice(Guid.Empty, Strings.General.None));
            combo.Items.AddRange(types);
            var selected = ids.ElementAtOrDefault(index);
            if (selected != Guid.Empty && types.All(type => type.Id != selected))
                combo.Items.Add(new TypeChoice(selected, Strings.MonsterTypeEditor.Missing.ToString(selected)));
            combo.SelectedItem = combo.Items.Cast<TypeChoice>().First(type => type.Id == selected);
        }
    }

    private void EditFields(object? sender, EventArgs e)
    {
        if (_loading || _selected == null) return;
        var primary = (_primaryType.SelectedItem as TypeChoice)?.Id ?? Guid.Empty;
        var secondary = (_secondaryType.SelectedItem as TypeChoice)?.Id ?? Guid.Empty;
        if (primary != Guid.Empty && primary == secondary)
        {
            // Restore the draft's valid selection instead of assigning one type twice.
            _loading = true;
            try { LoadTypeChoices(); }
            finally { _loading = false; }
            return;
        }
        _selected.TypeIds = new[] { primary, secondary }.Where(id => id != Guid.Empty).ToArray();
        _selected.Name = _name.Text;
        _selected.Description = _description.Text;
        _selected.Folder = _folder.Text;
        _selected.Sprite = _sprite.SelectedIndex <= 0 ? string.Empty : _sprite.Text;
        _selected.FrontBattleSprite = _frontBattleSprite.SelectedIndex <= 0 ? string.Empty : _frontBattleSprite.Text;
        _selected.BackBattleSprite = _backBattleSprite.SelectedIndex <= 0 ? string.Empty : _backBattleSprite.Text;
        _selected.BattleBackground = _battleBackground.SelectedIndex <= 0 ? string.Empty : _battleBackground.Text;
        _selected.BaseStats.HP = (int)_hp.Value;
        _selected.BaseStats.Attack = (int)_attack.Value;
        _selected.BaseStats.Defense = (int)_defense.Value;
        _selected.BaseStats.SpecialAttack = (int)_specialAttack.Value;
        _selected.BaseStats.SpecialDefense = (int)_specialDefense.Value;
        _selected.BaseStats.Speed = (int)_speed.Value;
        _speciesList.UpdateText(_selected.Name);
        if (primary == Guid.Empty && secondary != Guid.Empty)
        {
            // With one selected type it becomes primary; never leave a secondary-only assignment.
            _loading = true;
            try { LoadTypeChoices(); }
            finally { _loading = false; }
        }
        if (sender == _sprite) DrawSprite();
    }

    private void DrawSprite()
    {
        var old = _preview.Image;
        _preview.Image = null;
        old?.Dispose();
        if (_selected == null || string.IsNullOrEmpty(_selected.Sprite)) return;
        var path = Path.Combine("resources", "entities", _selected.Sprite);
        if (!File.Exists(path)) return;
        using var image = Image.FromFile(path);
        var width = image.Width / Math.Max(1, Options.Instance.Sprites.NormalFrames);
        var height = image.Height / Math.Max(1, Options.Instance.Sprites.Directions);
        if (width < 1 || height < 1) return;
        var frame = new Bitmap(width, height);
        using (var graphics = System.Drawing.Graphics.FromImage(frame))
            graphics.DrawImage(image, new Rectangle(0, 0, width, height), new Rectangle(0, 0, width, height), GraphicsUnit.Pixel);
        _preview.SizeMode = PictureBoxSizeMode.Zoom;
        _preview.Image = frame;
    }

    private void NewSpecies(object? sender, EventArgs e) => PacketSender.SendCreateObject(GameObjectType.MonsterSpecies);

    private void CopySpecies(object? sender, EventArgs e) => _copied = _selected?.JsonData;

    private void PasteSpecies(object? sender, EventArgs e)
    {
        if (_selected == null || _copied == null) return;
        _selected.Load(_copied, keepCreationTime: true);
        InitEditor();
    }

    private void UndoSpecies(object? sender, EventArgs e)
    {
        _selected?.RestoreBackup();
        InitEditor();
    }

    private void DeleteSpecies(object? sender, EventArgs e)
    {
        if (_selected != null && DarkMessageBox.ShowWarning(Strings.MonsterSpeciesEditor.DeletePrompt,
                Text, DarkDialogButton.YesNo, Icon) == DialogResult.Yes)
            PacketSender.SendDeleteObject(_selected);
    }

    private void SaveSpecies(object? sender, EventArgs e)
    {
        var changed = _drafts.Values.Where(item => item.JsonData != _originals[item.Id]).ToArray();
        if (changed.Any(item => !item.IsValid || item.TypeIds.Any(id => MonsterTypeDescriptor.Get(id) == null) ||
            item.Learnset.Any(entry => MonsterMoveDescriptor.Get(entry.MoveId) == null)))
        {
            DarkMessageBox.ShowWarning(Strings.MonsterSpeciesEditor.Invalid, Text);
            return;
        }
        foreach (var item in changed) PacketSender.SendSaveObject(item);
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _preview.Image?.Dispose();
        base.Dispose(disposing);
    }
}
