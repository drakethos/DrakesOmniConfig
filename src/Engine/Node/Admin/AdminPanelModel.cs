using System.Linq;
using DrakesOmniConfig.Engine.Node.Schema;
using DrakesOmniConfig.Engine.Node.Ui;
using UnityEngine;

namespace DrakesOmniConfig.Engine.Node.Admin;

/// <summary>
/// What the admin panel shows: the server's modlets and their rules with on/off buttons, plus Paste rules /
/// Reload / Close. Every button goes through <see cref="AdminChannel"/>. Opened by the Server build's F10 panel;
/// shared so automated tests can open the same panel and press its buttons.
/// </summary>
internal static class AdminPanelModel
{
    public const string PanelId = "omniconfig.admin";

    public static DialogModel Build(NodeRuleSet set, string status)
    {
        int active = set.Rules.Count(r => r.Active);
        var model = new DialogModel
        {
            Id = PanelId,
            Title = "OmniConfig — server modlets",
            Subtitle = status.Length > 0 ? status : $"{set.Modlets.Count} modlet(s), {active} of {set.Rules.Count} rule(s) on",
            Width = 640f
        };

        if (set.Modlets.Count == 0)
        {
            model.Text = "The server has no modlets. Build rules in the HTML Rules editor, copy the file, then use Paste rules.";
        }

        foreach (NodeModletInfo modlet in set.Modlets)
        {
            string id = modlet.Id;
            model.Rows.Add(new DialogRow
            {
                Label = modlet.DisplayName,
                Detail = $"{id} · {modlet.OverrideCount} field override(s) · {modlet.RuleCount} rule(s){(modlet.DisabledByAdmin ? " · OFF" : "")}",
                Muted = modlet.DisabledByAdmin,
                Buttons = { Toggle(!modlet.DisabledByAdmin, () => AdminChannel.Send("modlet.toggle", id)) }
            });

            foreach (NodeRule rule in set.Rules.Where(r => r.ModletId == id))
            {
                string key = rule.Key;
                string state = !rule.Enabled ? " · off in file" : rule.DisabledByAdmin ? " · OFF" : "";
                model.Rows.Add(new DialogRow
                {
                    Label = "    " + rule.Id,
                    Detail = $"    when {rule.Event} · {rule.Conditions.Count} condition(s) · {rule.Actions.Count} action(s){state}",
                    Muted = !rule.Active,
                    Buttons = rule.Enabled && !modlet.DisabledByAdmin
                        ? new() { Toggle(!rule.DisabledByAdmin, () => AdminChannel.Send("rule.toggle", key)) }
                        : new()
                });
            }
        }

        // Phase F: server flags (switch live) and other mods' settings set by modlets (read-only)
        if (set.Flags.Count > 0)
        {
            model.Rows.Add(new DialogRow { Label = "Flags", Detail = "Switching one updates config bindings, rules and flag-gated modlets now; field overrides on the next world load." });
            foreach (NodeFlagInfo flag in set.Flags)
            {
                string name = flag.Name;
                bool on = flag.On;
                model.Rows.Add(new DialogRow
                {
                    Label = "    " + name,
                    Detail = $"    {(on ? "on" : "off")}{(flag.Switched ? " · switched by an admin" : " · default")}",
                    Muted = !on,
                    Buttons = { Toggle(on, () => AdminChannel.Send("flag.set", name, on ? "off" : "on")) }
                });
            }
        }

        if (set.ConfigBindings.Count > 0)
        {
            model.Rows.Add(new DialogRow { Label = "Other mods' settings", Detail = $"{set.ConfigBindings.Count} binding(s) from modlets (configs:)" });
            foreach (string line in set.ConfigBindings)
            {
                model.Rows.Add(new DialogRow { Label = "", Detail = "    " + line, Muted = !line.Contains(": applied") });
            }
        }

        model.Buttons.Add(new DialogButton { Label = "Paste rules", Closes = false, OnClick = () => AdminChannel.Send("rules.put", "", GUIUtility.systemCopyBuffer ?? "") });
        model.Buttons.Add(new DialogButton { Label = "Reload", Closes = false, OnClick = () => AdminChannel.Send("reload") });
        model.Buttons.Add(new DialogButton { Label = "Close", Primary = true });
        return model;
    }

    private static DialogButton Toggle(bool on, System.Action send) =>
        new() { Label = on ? "Turn off" : "Turn on", Closes = false, OnClick = send };
}
