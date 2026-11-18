using System;
using System.Linq;
using DrakesOmniConfig.Config.Settings;
using DrakesOmniConfig.Engine.Node.Runtime;
using DrakesOmniConfig.Engine.Node.Schema;
using DrakesOmniConfig.Engine.Node.Ui;

namespace DrakesOmniConfig.Engine.Node.Library;

/// <summary>message (Valheim's own on-screen text) and popup (a window with buttons that run more actions).</summary>
internal static class UiActions
{
    public static void Register()
    {
        NodeRegistry.Register(new NodeDef
        {
            Kind = NodeKind.Action,
            Name = "message",
            Help = "Show a short message. Text can use {item}, {itemId}, {player}, {target}.",
            Shorthand = "text",
            Args =
            {
                NodeRegistry.Arg("text", "text", "What to show.", required: true),
                NodeRegistry.Arg("where", "choice", "center or corner.", false, "center", "center", "corner")
            },
            Run = (c, s) =>
            {
                var type = NodeValues.Raw(s, "where").Equals("corner", StringComparison.OrdinalIgnoreCase)
                    ? MessageHud.MessageType.TopLeft
                    : MessageHud.MessageType.Center;
                c.Player.Message(type, NodeValues.Text(c, s, "text"));
            }
        });

        NodeRegistry.Register(new NodeDef
        {
            Kind = NodeKind.Action,
            Name = "popup",
            Help = "Open a window. Each button can run its own actions. Text can use {item}, {itemId}, {player}, {target}.",
            Shorthand = "text",
            BranchKey = "buttons",
            Args =
            {
                NodeRegistry.Arg("title", "text", "Window title.", def: "Notice"),
                NodeRegistry.Arg("text", "text", "Body text.", required: true)
            },
            Run = Popup
        });
    }

    private static void Popup(NodeContext ctx, NodeStep step)
    {
        var model = new DialogModel
        {
            Id = "popup:" + ctx.Rule.Key,
            Title = NodeValues.Text(ctx, step, "title", "Notice"),
            Text = NodeValues.Text(ctx, step, "text"),
            Width = 460f
        };

        foreach (NodeBranch branch in step.Branches)
        {
            NodeBranch b = branch;
            model.Buttons.Add(new DialogButton
            {
                Label = NodeValues.Localize(b.Label.Length > 0 ? b.Label : "OK"),
                Primary = model.Buttons.Count == 0,
                OnClick = b.Steps.Count == 0 ? null : () =>
                {
                    try
                    {
                        RuleEngine.RunSteps(b.Steps, ctx);
                    }
                    catch (Exception ex)
                    {
                        ModLog.Error($"Rule '{ctx.Rule.Key}' popup button '{b.Label}' failed: {ex.Message}");
                    }
                }
            });
        }

        DialogService.Show(model);
    }
}
