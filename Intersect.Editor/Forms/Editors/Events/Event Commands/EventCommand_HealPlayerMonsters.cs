using DarkUI.Controls;
using DarkUI.Forms;
using Intersect.Editor.Localization;
using Intersect.Framework.Core.GameObjects.Events.Commands;
using Intersect.Framework.Core.GameObjects.Monsters;
namespace Intersect.Editor.Forms.Editors.Events.Event_Commands;
public sealed class EventCommandHealPlayerMonsters : UserControl
{
    private readonly HealPlayerMonstersCommand _command;
    private readonly FrmEvent? _editor;
    private readonly DarkComboBox _scope = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DarkComboBox _recovery = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    public EventCommandHealPlayerMonsters(HealPlayerMonstersCommand command, FrmEvent? editor)
    {
        _command = command; _editor = editor; Text = Strings.MonsterHealing.Title; Size = new Size(390, 200); MinimumSize = Size;
        BackColor = System.Drawing.Color.FromArgb(45, 45, 48); ForeColor = System.Drawing.Color.Gainsboro;
        _scope.Items.AddRange(new object[] { Strings.MonsterHealing.Party.ToString(), Strings.MonsterHealing.AllOwned.ToString() });
        _recovery.Items.AddRange(new object[] { Strings.MonsterHealing.HP.ToString(), Strings.MonsterHealing.PP.ToString(), Strings.MonsterHealing.Both.ToString(), Strings.MonsterHealing.Status.ToString(), Strings.MonsterHealing.Everything.ToString() });
        _scope.SelectedIndex = command.Scope == MonsterHealingScope.AllOwned ? 1 : 0;
        _recovery.SelectedIndex = command.Recovery switch { MonsterRecovery.HP => 0, MonsterRecovery.PP => 1, MonsterRecovery.Status => 3, MonsterRecovery.All => 4, _ => 2 };
        var fields = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14) }; fields.Controls.Add(_recovery); fields.Controls.Add(_scope); fields.Controls.Add(new DarkLabel { Text = Strings.MonsterHealing.Title, Dock = DockStyle.Top, Height = 26 });
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(5) };
        var save = new DarkButton { Text = Strings.EventChangeVital.okay, Width = 100 }; save.Click += (_, _) => SaveChanges();
        var cancel = new DarkButton { Text = Strings.EventChangeVital.cancel, Width = 100 }; cancel.Click += (_, _) => { if (_editor != null) _editor.CancelCommandEdit(); else Dispose(); };
        footer.Controls.Add(save); footer.Controls.Add(cancel); Controls.Add(fields); Controls.Add(footer);
    }
    private void SaveChanges()
    {
        _command.Scope = _scope.SelectedIndex == 1 ? MonsterHealingScope.AllOwned : MonsterHealingScope.Party;
        _command.Recovery = _recovery.SelectedIndex switch { 0 => MonsterRecovery.HP, 1 => MonsterRecovery.PP, 3 => MonsterRecovery.Status, 4 => MonsterRecovery.All, _ => MonsterRecovery.Both };
        if (_editor != null) _editor.FinishCommandEdit(); else Dispose();
    }
}
