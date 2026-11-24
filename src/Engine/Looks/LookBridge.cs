using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using DrakesOmniConfig.Config.Settings;
using DrakesOmniConfig.Forge;

namespace DrakesOmniConfig.Engine.Looks;

/// <summary>
/// Item looks (recolours, borrowed materials, parts) from modlets' looks/*.json, built by the Forge code compiled into
/// this mod (src/Forge). The server syncs the recipes as text; every side builds them locally from its own game files.
/// Looks are Forge reskins: they change an existing prefab in place, so they work live, including on a modlet clone
/// ("base": "PoisonSoup"). Jotunn is a soft dependency: without it looks stay off and everything else works.
/// </summary>
internal static class LookBridge
{
    private const string JotunnGuid = "com.jotunn.jotunn";
    private const string PackPrefix = "omniconfig.";

    private static bool _available;
    private static Dictionary<string, Dictionary<string, string>> _wanted = new();
    private static readonly HashSet<string> Applied = new();

    /// <summary>Modlet id -> the exact recipe text last built for it (to skip rebuilding unchanged looks).</summary>
    private static readonly Dictionary<string, string> AppliedText = new();

    public static bool Available => _available;

    private static string Text(Dictionary<string, string> recipes)
    {
        var sb = new System.Text.StringBuilder();
        foreach (KeyValuePair<string, string> r in recipes.OrderBy(r => r.Key, System.StringComparer.Ordinal))
        {
            sb.Append(r.Key).Append('\u0001').Append(r.Value).Append('\u0002');
        }

        return sb.ToString();
    }

    /// <summary>Forge is ready to build (the vanilla prefabs were available); always false without Jotunn.</summary>
    public static bool Ready => _available && ForgeLooks.Ready;

    /// <summary>No graphics device: a dedicated server (-nographics / batch mode).</summary>
    public static bool Headless => UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

    public static void Start(BaseUnityPlugin owner, ManualLogSource log)
    {
        if (!Chainloader.PluginInfos.ContainsKey(JotunnGuid))
        {
            ModLog.Info("Looks are off: Jotunn isn't installed (overrides, clones and rules work without it).");
            return;
        }

        ForgeLooks.Start(owner, log);
        _available = true;
    }

    /// <summary>The looks that should be on. Built now if the world's prefabs exist, else after clones are made (PrefabPassHook).</summary>
    public static void SetLooks(Dictionary<string, Dictionary<string, string>> looks)
    {
        _wanted = looks;
        if (ZNetScene.instance != null)
        {
            ApplyNow(allowSkip: true);
        }
    }

    /// <summary>Main menu (MenuPrebuild): builds these looks now, on the vanilla items and the clones made at the menu.</summary>
    public static void Prebuild(Dictionary<string, Dictionary<string, string>> looks, bool clonesReplaced)
    {
        _wanted = looks;
        ApplyNow(allowSkip: !clonesReplaced);
    }

    /// <summary>
    /// Builds the wanted looks. allowSkip: a modlet whose recipes are exactly what was last built is left as it is; the
    /// caller passes false when the prefabs those looks were built on may have been replaced (a clone made fresh).
    /// </summary>
    public static void ApplyNow(bool allowSkip = false)
    {
        if (!_available)
        {
            if (_wanted.Count > 0)
            {
                ModLog.Warning($"{_wanted.Sum(m => m.Value.Count)} look(s) skipped: Jotunn isn't installed.");
            }

            return;
        }

        // A headless (dedicated) server draws nothing: purely visual looks are synced to players as text but not built
        // here. A look that also changes gameplay (fields, crafting, components, collision, scale, name) is still built.
        bool headless = Headless;
        int skipped = 0;
        int unchanged = 0;
        foreach (KeyValuePair<string, Dictionary<string, string>> modlet in _wanted)
        {
            string text = Text(modlet.Value);
            if (allowSkip && AppliedText.TryGetValue(modlet.Key, out string? had) && had == text)
            {
                unchanged += modlet.Value.Count;
                continue;
            }

            AppliedText[modlet.Key] = text;
            Dictionary<string, string> recipes = modlet.Value;
            if (headless)
            {
                recipes = ForgeLooks.WithGameplay(modlet.Value);
                skipped += modlet.Value.Count - recipes.Count;
            }

            ModLog.Info($"Looks from '{modlet.Key}': {ForgeLooks.Load(PackPrefix + modlet.Key, recipes)}.");
            Applied.Add(modlet.Key);
        }

        if (skipped > 0)
        {
            ModLog.Info($"Headless server: {skipped} visual-only look(s) synced to players but not built here.");
        }

        if (unchanged > 0)
        {
            ModLog.Info($"Looks: {unchanged} already built (at the main menu) and unchanged.");
        }

        // a modlet that no longer has looks: Forge puts its reskins back
        foreach (string gone in Applied.Where(id => !_wanted.ContainsKey(id)).ToList())
        {
            ForgeLooks.Load(PackPrefix + gone, new Dictionary<string, string>());
            Applied.Remove(gone);
            AppliedText.Remove(gone);
            ModLog.Info($"Looks from '{gone}' removed.");
        }
    }
}

/// <summary>Everything that touches Forge (and so Jotunn) lives here, so nothing loads Jotunn types when it is absent.</summary>
internal static class ForgeLooks
{
    private static ForgeHost? _host;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Start(BaseUnityPlugin owner, ManualLogSource log)
    {
        _host = ForgeHost.Start(owner, new ForgeOptions { HotReload = false }, log);
        // a look on a clone made at the main menu: Jotunn's prefab cache can't see it there, so Forge asks us first
        ForgeApi.ExtraPrefab = Hooks.PrefabCloner.TryGet;
    }

    public static bool Ready
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        get => _host != null && _host.IsReady;
    }

    /// <summary>The recipes that change more than looks (kept on a headless server); unreadable ones are kept so Forge reports them.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Dictionary<string, string> WithGameplay(Dictionary<string, string> recipes)
    {
        var keep = new Dictionary<string, string>();
        foreach (KeyValuePair<string, string> file in recipes)
        {
            try
            {
                var r = DrakesOmniConfig.Forge.Format.RecipeSerializer.ReadRecipe(file.Value, new List<string>());
                bool visualOnly = r.Kind == DrakesOmniConfig.Forge.Format.RecipeKind.Reskin && r.Fields.Count == 0 && r.Behaviours.Count == 0 &&
                                  r.Craft == null && r.Snap == null && r.RemoveComponents.Count == 0 && r.AddComponents.Count == 0 &&
                                  r.Name == null && r.Description == null && r.Look.Collision == null && r.Look.Parts.Count == 0 && !r.Look.HasScale;
                if (!visualOnly)
                {
                    keep[file.Key] = file.Value;
                }
            }
            catch
            {
                keep[file.Key] = file.Value;
            }
        }

        return keep;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string Load(string packId, Dictionary<string, string> recipes) =>
        _host == null ? "Forge not started" : _host.LoadText(packId, recipes);
}
