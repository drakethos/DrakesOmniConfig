using HarmonyLib;
using UnityEngine;

namespace DrakesOmniConfig.Engine.Node.Ui;

/// <summary>Built-in fallback dialog (Unity IMGUI) for installs without DrakeModsLibs. Frees the mouse and blocks player input while open.</summary>
internal sealed class ImguiDialog : MonoBehaviour, IDialogRenderer
{
    private static ImguiDialog? _instance;
    private DialogModel? _model;
    private Vector2 _scroll;
    private Rect _rect;

    public bool IsOpen => _model != null;
    public string OpenId => _model?.Id ?? "";

    public static ImguiDialog Get()
    {
        if (_instance == null)
        {
            var go = new GameObject("DrakesOmniConfig.Dialog");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ImguiDialog>();
        }

        return _instance;
    }

    public void Show(DialogModel model)
    {
        _model = model;
        _scroll = Vector2.zero;
        float width = Mathf.Min(model.Width, Screen.width - 40f);
        _rect = new Rect((Screen.width - width) / 2f, Screen.height * 0.2f, width, 10f);
    }

    public void Close() => _model = null;

    private void Update()
    {
        if (_model != null && Input.GetKeyDown(KeyCode.Escape))
        {
            DialogService.Close();
        }
    }

    private void OnGUI()
    {
        if (_model == null)
        {
            return;
        }

        GUI.depth = -1000;
        _rect = GUILayout.Window(0x0DC0FF16, _rect, Draw, _model.Title, GUILayout.Width(_rect.width));
    }

    private void Draw(int id)
    {
        DialogModel? model = _model;
        if (model == null)
        {
            return;
        }

        if (model.Subtitle.Length > 0)
        {
            GUILayout.Label(model.Subtitle);
        }

        if (model.Text.Length > 0)
        {
            GUILayout.Label(model.Text, new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true });
        }

        if (model.Rows.Count > 0)
        {
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(Screen.height * 0.55f));
            foreach (DialogRow row in model.Rows)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(row.Detail.Length > 0 ? $"{row.Label}  <color=#9a8f7a>{row.Detail}</color>" : row.Label,
                    new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true });
                foreach (DialogButton b in row.Buttons)
                {
                    if (GUILayout.Button(b.Label, GUILayout.Width(90f)))
                    {
                        DialogService.Click(b);
                    }
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
        }

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        foreach (DialogButton b in model.Buttons.Count > 0 ? model.Buttons : new() { new DialogButton { Label = "OK" } })
        {
            if (GUILayout.Button(b.Label, GUILayout.MinWidth(90f)))
            {
                DialogService.Click(b);
            }
        }

        GUILayout.EndHorizontal();
        GUI.DragWindow();
    }

    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    private static class BlockInputPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (_instance == null || !_instance.IsOpen)
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
    private static class FreeMousePatch
    {
        private static void Postfix()
        {
            if (_instance != null && _instance.IsOpen)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
    }
}
