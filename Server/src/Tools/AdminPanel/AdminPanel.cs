using BepInEx.Configuration;
using DrakesOmniConfig.Engine.Node.Admin;
using DrakesOmniConfig.Engine.Node.Runtime;
using DrakesOmniConfig.Engine.Node.Ui;
using UnityEngine;

namespace DrakesOmniConfig.Tools.AdminPanel;

/// <summary>
/// In-game admin panel (Server build only), opened with a hotkey: the server's modlets and rules with on/off
/// switches, Reload, and "Paste rules" for a file copied from the HTML Rules editor. Works for the host and
/// for an admin connected to a dedicated server — the server checks every change against its admin list.
/// Content: <see cref="AdminPanelModel"/>.
/// </summary>
internal sealed class AdminPanel : MonoBehaviour
{
    private ConfigEntry<KeyboardShortcut>? _key;
    private string _status = "";

    public void Init(ConfigFile config)
    {
        _key = config.Bind("Admin", "PanelKey", new KeyboardShortcut(KeyCode.F10),
            "Opens the OmniConfig admin panel (server admins and the host only).");
        AdminChannel.Replied += (ok, message) =>
        {
            _status = (ok ? "" : "Refused: ") + message;
            Refresh();
        };
        RuleEngine.Changed += Refresh;
    }

    private void Update()
    {
        if (_key == null || !_key.Value.IsDown() || ZNet.instance == null || Player.m_localPlayer == null)
        {
            return;
        }

        if (DialogService.OpenId == AdminPanelModel.PanelId)
        {
            DialogService.Close();
            return;
        }

        if (!AdminChannel.CanAdmin)
        {
            Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Only server admins can open the OmniConfig panel.");
            return;
        }

        _status = "";
        DialogService.Show(AdminPanelModel.Build(RuleEngine.Current, _status));
    }

    private void Refresh()
    {
        if (DialogService.OpenId == AdminPanelModel.PanelId)
        {
            DialogService.Show(AdminPanelModel.Build(RuleEngine.Current, _status));
        }
    }
}
