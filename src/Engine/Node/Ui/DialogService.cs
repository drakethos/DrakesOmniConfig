using System;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using DrakesOmniConfig.Config.Settings;
using UnityEngine;

namespace DrakesOmniConfig.Engine.Node.Ui;

/// <summary>
/// Shows one dialog at a time. Uses DrakeModsLibs' window when Libs is installed (soft dependency — this mod
/// never requires it), otherwise <see cref="ImguiDialog"/>. If the Libs window throws (an API change in a
/// newer Libs), it falls back for the rest of the session instead of failing the rule.
/// </summary>
internal static class DialogService
{
    public const string LibsGuid = "com.drakemods.libs";

    private static IDialogRenderer? _renderer;
    private static bool _libsFailed;

    /// <summary>Tests set this to exercise the built-in dialog on a profile that has DrakeModsLibs.</summary>
    public static bool ForceBuiltIn { get; set; }

    public static bool LibsAvailable => !ForceBuiltIn && !_libsFailed && Chainloader.PluginInfos.ContainsKey(LibsGuid);

    /// <summary>"libs" or "builtin": which renderer the dialog on screen uses.</summary>
    public static string Renderer => _renderer is ImguiDialog ? "builtin" : _renderer != null ? "libs" : "";
    public static bool IsOpen => _renderer?.IsOpen == true;
    public static string OpenId => _renderer?.IsOpen == true ? _renderer.OpenId : "";

    /// <summary>The model on screen now (tests click its buttons through this).</summary>
    public static DialogModel? Current { get; private set; }

    public static void Show(DialogModel model)
    {
        Close();
        Current = model;
        if (LibsAvailable)
        {
            try
            {
                ShowLibs(model);
                return;
            }
            catch (Exception ex)
            {
                _libsFailed = true;
                ModLog.Warning($"DrakeModsLibs window failed ({ex.GetType().Name}: {ex.Message}); using the built-in dialog instead.");
            }
        }

        _renderer = ImguiDialog.Get();
        _renderer.Show(model);
    }

    public static void Close()
    {
        _renderer?.Close();
        _renderer = null;
        Current = null;
    }

    /// <summary>Runs a button the way a click would (also used by automated tests).</summary>
    public static void Click(DialogButton button)
    {
        try
        {
            button.OnClick?.Invoke();
        }
        finally
        {
            if (button.Closes)
            {
                Close();
            }
        }
    }

    // Kept out of Show() so the JIT only touches DrakeModsLibs types when Libs is actually loaded.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ShowLibs(DialogModel model)
    {
        var libs = new LibsDialog();
        libs.Show(model);
        _renderer = libs;
    }
}
