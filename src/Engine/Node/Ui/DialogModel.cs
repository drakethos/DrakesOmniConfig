using System;
using System.Collections.Generic;

namespace DrakesOmniConfig.Engine.Node.Ui;

/// <summary>
/// Everything a popup or the admin panel shows: a title, body text, optional rows (a label with its own
/// buttons) and footer buttons. Renderer-neutral, so the same model draws with DrakeModsLibs' window or the
/// built-in fallback.
/// </summary>
internal sealed class DialogModel
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Text { get; set; } = "";
    public List<DialogRow> Rows { get; set; } = new();
    public List<DialogButton> Buttons { get; set; } = new();
    public float Width { get; set; } = 520f;
}

internal sealed class DialogRow
{
    public string Label { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool Muted { get; set; }
    public List<DialogButton> Buttons { get; set; } = new();
}

internal sealed class DialogButton
{
    public string Label { get; set; } = "";
    public bool Primary { get; set; }

    /// <summary>Close the dialog after <see cref="OnClick"/> runs (popup buttons do; panel toggles don't).</summary>
    public bool Closes { get; set; } = true;

    public Action? OnClick { get; set; }
}

internal interface IDialogRenderer
{
    bool IsOpen { get; }
    string OpenId { get; }
    void Show(DialogModel model);
    void Close();
}
