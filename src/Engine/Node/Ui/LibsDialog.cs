using DrakeModsLibs.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace DrakesOmniConfig.Engine.Node.Ui;

/// <summary>
/// The dialog drawn with DrakeModsLibs' shared window (same look as the other Drakes mods). Only ever
/// constructed when Libs is loaded — see <see cref="DialogService"/>.
/// </summary>
internal sealed class LibsDialog : IDialogRenderer
{
    private UkWindow? _window;
    private string _id = "";

    public bool IsOpen => _window?.IsOpen == true;
    public string OpenId => IsOpen ? _id : "";

    public void Show(DialogModel model)
    {
        _id = model.Id;
        _window = new UkWindow("DrakesOmniConfig." + (model.Id.Length > 0 ? model.Id : "Dialog"), model.Width, UkWindow.DialogLayer);
        _window.SetHeader(model.Title, model.Subtitle.Length > 0 ? model.Subtitle : null);

        if (model.Text.Length > 0)
        {
            _window.Body.Add(UkControls.Text(model.Text, 16, UkTheme.TextBody));
        }

        if (model.Rows.Count > 0)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = 560;
            foreach (DialogRow row in model.Rows)
            {
                var line = new VisualElement().Row();
                line.style.marginTop = 6;
                var labels = new VisualElement().Column().Grow();
                labels.Add(UkControls.Text(row.Label, 16, row.Muted ? UkTheme.TextMuted : UkTheme.Text));
                if (row.Detail.Length > 0)
                {
                    labels.Add(UkControls.Text(row.Detail, 13, UkTheme.TextMuted));
                }

                line.Add(labels);
                foreach (DialogButton b in row.Buttons)
                {
                    var button = UkControls.MakeButton(b.Label, () => DialogService.Click(b), b.Primary ? UkButtonKind.Primary : UkButtonKind.Secondary);
                    button.style.marginLeft = 8;
                    line.Add(button);
                }

                scroll.Add(line);
            }

            _window.Body.Add(scroll);
        }

        var footer = new VisualElement().Row().Grow();
        footer.style.justifyContent = Justify.FlexEnd;
        var buttons = model.Buttons.Count > 0 ? model.Buttons : new() { new DialogButton { Label = "OK", Primary = true } };
        foreach (DialogButton b in buttons)
        {
            var button = UkControls.MakeButton(b.Label, () => DialogService.Click(b), b.Primary ? UkButtonKind.Primary : UkButtonKind.Secondary);
            button.style.marginLeft = 8;
            footer.Add(button);
        }

        _window.Footer.Add(footer);
        _window.ShowFooter(true);
        _window.Closed = () => DialogService.Close();
        _window.Open();
    }

    public void Close()
    {
        UkWindow? window = _window;
        _window = null;
        window?.CloseSilent();
    }
}
