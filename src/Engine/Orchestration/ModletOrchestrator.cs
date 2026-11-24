using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DrakesOmniConfig.Config.Modlets.Management;
using DrakesOmniConfig.Config.Modlets.Schema;
using DrakesOmniConfig.Config.Settings;
using DrakesOmniConfig.Engine.Fields.Application;
using DrakesOmniConfig.Engine.Hooks;
using DrakesOmniConfig.Engine.Node.Admin;
using DrakesOmniConfig.Engine.Node.Runtime;
using DrakesOmniConfig.Engine.Node.Schema;
using DrakesOmniConfig.Sync.Authority;
using HarmonyLib;
using YamlDotNet.Serialization;

namespace DrakesOmniConfig.Engine.Orchestration;

/// <summary>
/// Top-level coordinator, constructed and called from the plugin's Awake(). Owns the server-vs-client
/// branch so every other class stays agnostic of which side it's running on.
/// </summary>
internal sealed class ModletOrchestrator : IAdminHandler
{
    private const string DefaultPutModlet = "admin-rules";
    private static readonly Regex SafeName = new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.Compiled);

    private readonly ModletManager _modletManager;
    private readonly FieldPatcher _fieldPatcher;
    private readonly ModletAuthority _authority;
    private readonly NodeState _state = new();
    private string _modletsFolder = "";

    // Same two-halves shape as PrefabPassHook/GlobalRebindHook: Bootstrap() only runs at plugin Awake()
    // time, before ZNet.instance exists, so the real IsServer() branch has to wait for ZNet's own Awake.
    private static ModletOrchestrator? _current;

    /// <summary>The current config bindings (resolved on the authoritative side, received on a client). For test hooks.</summary>
    internal static IReadOnlyList<ResolvedConfigBinding> CurrentConfigBindings =>
        _current?._modletManager.ResolvedConfigBindings ?? (IReadOnlyList<ResolvedConfigBinding>)System.Array.Empty<ResolvedConfigBinding>();

    /// <summary>The authoritative side's resolved set (empty before a world loads). For test hooks.</summary>
    internal static IReadOnlyList<ResolvedOverride> CurrentResolved =>
        _current?._modletManager.ResolvedOverrides ?? (IReadOnlyList<ResolvedOverride>)System.Array.Empty<ResolvedOverride>();

    public ModletOrchestrator(ModletManager modletManager, FieldPatcher fieldPatcher, ModletAuthority authority)
    {
        _modletManager = modletManager;
        _fieldPatcher = fieldPatcher;
        _authority = authority;
        _authority.ResolvedReceived += OnResolvedReceived;
        _authority.RulesReceived += OnRulesReceived;
        _authority.LooksReceived += OnLooksReceived;
        _authority.ConfigsReceived += OnConfigsReceived;
    }

    public string ModletsFolder => _modletsFolder;

    // modletsFolder comes from MainConfig.ModletsFolder (BepInEx/config/DrakesOmniConfig/Modlets by
    // default). Only remembers it here — ZNet.instance doesn't exist yet at plugin Awake() time, so the
    // actual IsServer() branch happens in OnZNetAwake below.
    public void Bootstrap(string modletsFolder)
    {
        _modletsFolder = modletsFolder;
        _current = this;
    }

    [HarmonyPatch(typeof(ZNet), "Awake")]
    private static class ZNet_Awake_Patch
    {
        private static void Postfix()
        {
            _current?.OnZNetAwake();
        }
    }

    // Re-runs on every ZNet.Awake (world load/rejoin), same as PrefabPassHook/GlobalRebindHook re-apply
    // on every relevant lifecycle event rather than latching "done once" — cheap, and picks up modlet
    // file edits made between sessions.
    // ---------------------------------------------------------------- start-up preload

    /// <summary>What the background load produced: the parsed/resolved set lives in the manager; these are its sync payloads.</summary>
    private sealed class Preloaded
    {
        public string Fingerprint = "";
        public string Overrides = "";
        public string Rules = "";
        public string Looks = "";
        public double Ms;
    }

    private Task<Preloaded?>? _preload;

    /// <summary>How the authoritative side loaded at the last world start: "preload" (read in the background at plugin load) or "load". For tests.</summary>
    internal static string WorldStartLoadPath { get; private set; } = "";

    /// <summary>
    /// Plugin Awake, after the node library is registered: reads, resolves and serialises the modlets on a worker thread
    /// (pure data, no Unity calls), so the first world start only publishes. The result is used once, and only if no
    /// modlet file changed since (see <see cref="Fingerprint"/>); anything else falls back to the normal load. Nothing is
    /// written to disk, so there is no cache to go stale or to clear.
    /// </summary>
    public void StartPreload()
    {
        string folder = _modletsFolder;
        if (folder.Length == 0 || !Directory.Exists(folder))
        {
            return;
        }

        _preload = Task.Run<Preloaded?>(() =>
        {
            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var result = new Preloaded { Fingerprint = Fingerprint(folder) };
                _state.Load(folder);
                _modletManager.LoadAndResolve(folder, _state);
                result.Overrides = Sync.Transport.ResolvedOverrideSerializer.Serialize(_modletManager.ResolvedOverrides);
                result.Rules = Sync.Transport.NodeRuleSetSerializer.Serialize(_modletManager.Rules);
                result.Looks = Sync.Transport.LooksSerializer.Serialize(_modletManager.Looks);
                result.Ms = clock.Elapsed.TotalMilliseconds;
                return result;
            }
            catch (Exception ex)
            {
                ModLog.Warning($"Background modlet load failed; loading at world start instead. {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        });
    }

    /// <summary>
    /// For the main-menu prebuild: the set this side expects to use (the start-up preload, or the last world's set when
    /// back at the menu), or null while the preload is still running or nothing is loaded. Read-only; does not take the preload.
    /// </summary>
    internal static (IReadOnlyList<ResolvedOverride> Resolved, Dictionary<string, Dictionary<string, string>> Looks, FieldPatcher Patcher)? PrebuildSource()
    {
        ModletOrchestrator? self = _current;
        if (self == null)
        {
            return null;
        }

        if (self._preload != null && (!self._preload.IsCompleted || self._preload.IsFaulted || self._preload.Result == null))
        {
            return null;
        }

        if (self._modletManager.ResolvedOverrides.Count == 0 && self._modletManager.Looks.Count == 0)
        {
            return null;
        }

        return (self._modletManager.ResolvedOverrides, self._modletManager.Looks, self._fieldPatcher);
    }

    /// <summary>Waits for the preload (normally long finished) and hands it over once; the manager is main-thread-only after this.</summary>
    private Preloaded? TakePreload()
    {
        if (_preload == null)
        {
            return null;
        }

        Task<Preloaded?> task = _preload;
        _preload = null;
        try
        {
            return task.Result;
        }
        catch (Exception ex)
        {
            ModLog.Warning($"Background modlet load failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Every modlet file's path, size and write time, plus the admin state file: any edit changes it.</summary>
    private static string Fingerprint(string folder)
    {
        var sb = new System.Text.StringBuilder();
        foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            var info = new FileInfo(file);
            sb.Append(file).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('\n');
        }

        foreach (string extra in new[] { DrakesOmniConfig.Config.Flags.FlagState.PathFor(folder) })
        {
            if (File.Exists(extra))
            {
                var info = new FileInfo(extra);
                sb.Append(extra).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('\n');
            }
        }

        string state = NodeState.PathFor(folder);
        if (File.Exists(state))
        {
            var info = new FileInfo(state);
            sb.Append(state).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks);
        }

        return sb.ToString();
    }

    private void OnZNetAwake()
    {
        // the preload owns the manager until it finishes; from here on only the main thread touches it
        Preloaded? preloaded = TakePreload();
        AdminChannel.Register(this);
        if (ZNet.instance != null && ZNet.instance.IsServer())
        {
            ModLog.Info($"Bootstrap: authoritative side (server or hosting) — loading modlets from '{_modletsFolder}'.");
            if (preloaded != null && preloaded.Fingerprint == Fingerprint(_modletsFolder))
            {
                WorldStartLoadPath = "preload";
                UsePreload(preloaded);
            }
            else
            {
                WorldStartLoadPath = "load";
                if (preloaded != null)
                {
                    ModLog.Info("Modlet files changed since start-up; loading them again.");
                }

                LoadPublish();
            }
        }
        else
        {
            RuleEngine.SetRules(new NodeRuleSet());
            ModLog.Info("Bootstrap: joined-client side — waiting for the server's resolved overrides.");
        }
    }

    /// <summary>The first world start with an unchanged modlet folder: the set was loaded and serialised in the background.</summary>
    private void UsePreload(Preloaded preloaded)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        ApplyAll();
        ApplyConfigBindings();
        AnnotateRules();
        RuleEngine.SetRules(_modletManager.Rules);
        ModLog.Info($"Rules: {RuleEngine.ActiveCount} active of {_modletManager.Rules.Rules.Count} in {_modletManager.Rules.Modlets.Count} modlet(s).");
        // rules are written again here: they now carry the flags and binding results for the admin panel
        _authority.PublishPrepared(preloaded.Overrides, Sync.Transport.NodeRuleSetSerializer.Serialize(_modletManager.Rules), preloaded.Looks);
        Flags.GlobalKeyMirror.Apply(_modletManager.Flags);
        Looks.LookBridge.SetLooks(_modletManager.Looks);
        ModLog.Info($"Timing (ms): loaded in the background at start-up ({preloaded.Ms:0}); world start {clock.Elapsed.TotalMilliseconds:0}.");
    }

    private void LoadPublish()
    {
        // one timing line per load, so a slow modlet set shows where its time goes
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double last = 0;
        var phases = new List<string>();
        void Mark(string phase)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            phases.Add($"{phase} {now - last:0}");
            last = now;
        }

        _state.Load(_modletsFolder);
        _modletManager.LoadAndResolve(_modletsFolder, _state);
        Mark("load+resolve");
        ApplyAll();
        ApplyConfigBindings();
        AnnotateRules();
        RuleEngine.SetRules(_modletManager.Rules);
        Mark("prepare");
        ModLog.Info($"Rules: {RuleEngine.ActiveCount} active of {_modletManager.Rules.Rules.Count} in {_modletManager.Rules.Modlets.Count} modlet(s).");
        _authority.PublishAll(_modletManager.ResolvedOverrides);
        _authority.PublishRules(_modletManager.Rules);
        Flags.GlobalKeyMirror.Apply(_modletManager.Flags);
        Mark("publish");
        Looks.LookBridge.SetLooks(_modletManager.Looks);
        Mark("looks");
        _authority.PublishLooks(_modletManager.Looks);
        Mark("publish looks");
        ModLog.Info($"Timing (ms): {string.Join(", ", phases)}; total {clock.Elapsed.TotalMilliseconds:0}.");
    }

    // ---------------------------------------------------------------- other mods' configs (Phase F)

    /// <summary>
    /// Authoritative side: sets every bound entry (in memory, from its baseline) and publishes the ones OmniConfig must
    /// deliver; entries a mod syncs itself reach players through that mod's own sync once set here.
    /// </summary>
    private void ApplyConfigBindings()
    {
        IReadOnlyList<ResolvedConfigBinding> bindings = _modletManager.ResolvedConfigBindings;
        List<ResolvedConfigBinding> deliver = ModConfigs.ConfigBindingApplier.Apply(bindings, authority: true);
        _authority.PublishConfigs(deliver);
        if (bindings.Count > 0)
        {
            ModLog.Info($"Config bindings: {ModConfigs.ConfigBindingApplier.Status.Count(s => s.Value.StartsWith("applied", StringComparison.Ordinal))} of {bindings.Count} applied; {deliver.Count} sent to players by OmniConfig.");
        }
    }

    /// <summary>Flags and binding results ride along with the rules, so an admin's F10 panel can show them.</summary>
    private void AnnotateRules()
    {
        NodeRuleSet rules = _modletManager.Rules;
        rules.Flags = _modletManager.Flags.All.Select(f => new NodeFlagInfo { Name = f.Name, On = f.On, Switched = f.Switched }).ToList();
        rules.ConfigBindings = _modletManager.ResolvedConfigBindings.Count == 0 ? new List<string>() : ConfigsList().Split('\n').ToList();
    }

    private void OnConfigsReceived(IReadOnlyList<ResolvedConfigBinding> bindings)
    {
        _modletManager.AdoptConfigBindings(bindings);
        ModConfigs.ConfigBindingApplier.Apply(bindings, authority: false);
        ModLog.Info($"Received {bindings.Count} config binding(s) from the server.");
    }

    /// <summary>Leaving a world (client or host): every bound entry goes back to the mod's own value.</summary>
    [HarmonyPatch(typeof(ZNet), "Shutdown")]
    private static class ZNet_Shutdown_Patch
    {
        private static bool Prepare() => AccessTools.Method(typeof(ZNet), "Shutdown") != null;

        private static void Postfix() => ModConfigs.ConfigBindingApplier.RestoreAll();
    }

    private void OnLooksReceived(Dictionary<string, Dictionary<string, string>> looks)
    {
        if (ZNet.instance != null && ZNet.instance.IsServer())
        {
            return; // the authoritative side already applied what it published
        }

        _modletManager.AdoptLooks(looks);
        Looks.LookBridge.SetLooks(looks);
        ModLog.Info($"Received looks from the server: {looks.Sum(m => m.Value.Count)} recipe(s) in {looks.Count} modlet(s).");
    }

    private void OnResolvedReceived(IReadOnlyList<ResolvedOverride> resolved)
    {
        ModLog.Info($"Received {resolved.Count} resolved override(s) from the server.");
        _modletManager.AdoptResolved(resolved);
        ApplyAll();
        // A joined client usually gets the server's set after its world's prefabs already exist: build clones and
        // overrides now instead of on the next world load (the authoritative side applies at ZNetScene.Awake).
        if (ZNet.instance != null && !ZNet.instance.IsServer() && ZNetScene.instance != null)
        {
            PrefabPassHook.ApplyNow();
        }
    }

    private void OnRulesReceived(NodeRuleSet rules)
    {
        _modletManager.AdoptRules(rules);
        RuleEngine.SetRules(rules);
        ModLog.Info($"Received {rules.Rules.Count} rule(s) from the server ({RuleEngine.ActiveCount} active).");
    }

    public void ApplyAll()
    {
        var prefabOverrides = new List<ResolvedOverride>();
        var globalOverrides = new List<ResolvedOverride>();

        foreach (ResolvedOverride resolved in _modletManager.ResolvedOverrides)
        {
            if (resolved.Target.Scope == ModletScope.Prefab)
            {
                prefabOverrides.Add(resolved);
            }
            else
            {
                globalOverrides.Add(resolved);
            }
        }

        ModLog.Info($"ApplyAll: {prefabOverrides.Count} prefab-scope, {globalOverrides.Count} global-scope override(s) resolved.");
        foreach (ResolvedOverride resolved in _modletManager.ResolvedOverrides)
        {
            ModLog.Debug(
                $"  {resolved.Target.TypeName}/{resolved.Target.ItemId}.{resolved.FieldName} -> won by '{resolved.WinningModletId}'");
        }

        PrefabPassHook.Prepare(prefabOverrides, _fieldPatcher);
        GlobalRebindHook.Prepare(globalOverrides, _fieldPatcher);
    }

    // ---------------------------------------------------------------- admin commands (authoritative side)

    public (bool ok, string message) HandleAdmin(string command, string arg, string payload)
    {
        switch (command)
        {
            case "reload":
                LoadPublish();
                return (true, Summary("Reloaded."));

            case "modlet.toggle":
                if (_modletManager.Rules.Modlets.All(m => !m.Id.Equals(arg, StringComparison.OrdinalIgnoreCase)))
                {
                    return (false, $"No modlet '{arg}' on the server.");
                }

                bool modletOn = NodeState.Toggle(_state.DisabledModlets, arg);
                _state.Save();
                LoadPublish();
                return (true, $"Modlet '{arg}' {(modletOn ? "on" : "off")}. Rules change now; field overrides on the next world load.");

            case "rule.toggle":
                if (_modletManager.Rules.Rules.All(r => !r.Key.Equals(arg, StringComparison.OrdinalIgnoreCase)))
                {
                    return (false, $"No rule '{arg}' on the server.");
                }

                bool ruleOn = NodeState.Toggle(_state.DisabledRules, arg);
                _state.Save();
                LoadPublish();
                return (true, $"Rule '{arg}' {(ruleOn ? "on" : "off")}.");

            case "flag.set":
                return SetFlag(arg, payload);

            case "flag.list":
                return (true, FlagList());

            case "configs.list":
                return (true, ConfigsList());

            case "rules.put":
                return PutRules(payload);

            case "rules.delete":
                return DeleteRules(arg);

            case "looks.put":
                return PutLook(arg, payload);

            case "looks.delete":
                return DeleteLook(arg);

            default:
                return (false, $"Unknown command '{command}'.");
        }
    }

    // ---------------------------------------------------------------- flags and config bindings (Phase F)

    // arg = flag name, payload = "true"/"on" | "false"/"off" | "default" (forget the switch, back to flags.yaml)
    private (bool ok, string message) SetFlag(string name, string value)
    {
        name = name.Trim();
        if (!_modletManager.Flags.IsDeclared(name))
        {
            return (false, $"No flag '{name}' declared in Modlets/{DrakesOmniConfig.Config.Flags.FlagSet.FileName}.");
        }

        string v = value.Trim().ToLowerInvariant();
        bool? on;
        if (v is "true" or "on" or "1")
        {
            on = true;
        }
        else if (v is "false" or "off" or "0")
        {
            on = false;
        }
        else if (v is "default" or "")
        {
            on = null;
        }
        else
        {
            return (false, $"flag.set: '{value}' is not on, off or default.");
        }
        DrakesOmniConfig.Config.Flags.FlagState.Set(_modletsFolder, name, on);
        LoadPublish();
        return (true, $"Flag '{name}' {(_modletManager.Flags.IsOn(name) ? "on" : "off")}{(on == null ? " (default)" : "")}. Config bindings, rules and gated modlets change now; prefab field overrides on the next world load.");
    }

    private string FlagList()
    {
        var lines = _modletManager.Flags.All.Select(f => $"{f.Name}: {(f.On ? "on" : "off")}{(f.Switched ? " (switched by an admin)" : "")}").ToList();
        return lines.Count == 0 ? $"No flags declared (Modlets/{DrakesOmniConfig.Config.Flags.FlagSet.FileName})." : string.Join("\n", lines);
    }

    private string ConfigsList()
    {
        IReadOnlyList<ResolvedConfigBinding> bindings = _modletManager.ResolvedConfigBindings;
        if (bindings.Count == 0)
        {
            return "No config bindings.";
        }

        return string.Join("\n", bindings.Select(b =>
            $"{b} = {b.Expression.Operator} {b.Expression.Operand}{(b.Expression.IsPercentage ? "%" : "")} (modlet {b.WinningModletId}): " +
            (ModConfigs.ConfigBindingApplier.Status.TryGetValue(b.Identity, out string? status) ? status : "not applied")));
    }

    // arg "modlet/file": a Forge recipe saved as Modlets/<modlet>/looks/<file>.json, then reloaded and published.
    private (bool ok, string message) PutLook(string arg, string json)
    {
        string[] parts = arg.Split('/');
        if (parts.Length != 2 || parts[0].Trim().Length == 0 || parts[1].Trim().Length == 0)
        {
            return (false, "looks.put needs 'modlet/file' (e.g. com.myserver.items/poison-soup).");
        }

        var problems = new List<string>();
        try
        {
            DrakesOmniConfig.Forge.Format.RecipeSerializer.ReadRecipe(json, problems);
        }
        catch (Exception ex)
        {
            return (false, $"Not a Forge recipe: {ex.Message}");
        }

        if (problems.Count > 0)
        {
            return (false, "Look refused: " + string.Join("; ", problems));
        }

        string modlet = parts[0].Trim();
        string file = Path.GetFileNameWithoutExtension(parts[1].Trim());
        if (!SafeName.IsMatch(modlet) || !SafeName.IsMatch(file) || modlet.Contains("..") || file.Contains(".."))
        {
            return (false, "Modlet and file names may only use letters, digits, '.', '-' and '_'.");
        }

        string folder = Path.Combine(_modletsFolder, modlet);
        Directory.CreateDirectory(folder);
        string manifest = Path.Combine(folder, "manifest.yaml");
        if (!File.Exists(manifest))
        {
            File.WriteAllText(manifest, $"schemaVersion: 1\ndisplayName: {modlet}\nenabled: true\npriority: 0\n");
        }
        string looks = Path.Combine(folder, Config.Modlets.Loading.ModletLoader.LooksFolderName);
        Directory.CreateDirectory(looks);
        File.WriteAllText(Path.Combine(looks, file + ".json"), json);
        LoadPublish();
        return (true, Summary($"Saved look {modlet}/looks/{file}.json."));
    }

    private (bool ok, string message) DeleteLook(string arg)
    {
        string[] parts = arg.Split('/');
        if (parts.Length != 2 || !SafeName.IsMatch(parts[0].Trim()) || !SafeName.IsMatch(parts[1].Trim()) || arg.Contains(".."))
        {
            return (false, "looks.delete needs 'modlet/file' (letters, digits, '.', '-', '_').");
        }

        string path = Path.Combine(_modletsFolder, parts[0].Trim(), Config.Modlets.Loading.ModletLoader.LooksFolderName, Path.GetFileNameWithoutExtension(parts[1].Trim()) + ".json");
        if (!File.Exists(path))
        {
            return (false, $"No look '{arg}'.");
        }

        File.Delete(path);
        LoadPublish();
        return (true, Summary($"Deleted look {arg}."));
    }

    private (bool ok, string message) PutRules(string yaml)
    {
        string modlet = DefaultPutModlet;
        string file = "pasted";
        try
        {
            if (new DeserializerBuilder().Build().Deserialize<object>(yaml) is Dictionary<object, object> map)
            {
                if (map.TryGetValue("modlet", out object? m) && m != null)
                {
                    modlet = m.ToString().Trim();
                }

                if (map.TryGetValue("file", out object? f) && f != null)
                {
                    file = Path.GetFileNameWithoutExtension(f.ToString().Trim());
                }
            }
        }
        catch (Exception ex)
        {
            return (false, "That isn't valid YAML: " + ex.Message);
        }

        if (!SafeName.IsMatch(modlet) || !SafeName.IsMatch(file) || modlet.Contains("..") || file.Contains(".."))
        {
            return (false, "Modlet and file names may only use letters, digits, '.', '-' and '_'.");
        }

        List<NodeRule> rules = _modletManager.NodeLoader.Parse(yaml, modlet, file + ".yaml", out List<string> errors, logErrors: false);
        if (errors.Count > 0)
        {
            return (false, "Not saved. " + string.Join(" ", errors.Take(3)));
        }

        if (rules.Count == 0)
        {
            return (false, "Not saved: the file has no rules.");
        }

        string modletFolder = Path.Combine(_modletsFolder, modlet);
        string nodesFolder = Path.Combine(modletFolder, "nodes");
        Directory.CreateDirectory(nodesFolder);
        string manifest = Path.Combine(modletFolder, "manifest.yaml");
        if (!File.Exists(manifest))
        {
            File.WriteAllText(manifest, $"schemaVersion: 1\ndisplayName: {modlet}\nenabled: true\npriority: 0\n");
        }

        File.WriteAllText(Path.Combine(nodesFolder, file + ".yaml"), yaml);
        LoadPublish();
        return (true, Summary($"Saved {rules.Count} rule(s) to {modlet}/nodes/{file}.yaml."));
    }

    /// <summary>arg = "modlet/file" (no extension). Removes that one rules file; a modlet left with only a manifest goes too.</summary>
    private (bool ok, string message) DeleteRules(string arg)
    {
        string[] parts = arg.Split('/');
        if (parts.Length != 2 || !SafeName.IsMatch(parts[0]) || !SafeName.IsMatch(parts[1]) || arg.Contains(".."))
        {
            return (false, "Give the file as modlet/file, e.g. admin-rules/pasted.");
        }

        string modletFolder = Path.Combine(_modletsFolder, parts[0]);
        string file = Path.Combine(modletFolder, "nodes", parts[1] + ".yaml");
        if (!File.Exists(file))
        {
            return (false, $"No rules file {parts[0]}/nodes/{parts[1]}.yaml on the server.");
        }

        File.Delete(file);
        string nodes = Path.Combine(modletFolder, "nodes");
        bool onlyManifest = Directory.GetFileSystemEntries(nodes).Length == 0 &&
                            Directory.GetFileSystemEntries(modletFolder).All(p => Path.GetFileName(p) is "nodes" or "manifest.yaml");
        if (onlyManifest)
        {
            Directory.Delete(modletFolder, recursive: true);
        }

        LoadPublish();
        return (true, Summary($"Deleted {parts[0]}/nodes/{parts[1]}.yaml{(onlyManifest ? " and the now-empty modlet" : "")}."));
    }

    private string Summary(string lead) =>
        $"{lead} {_modletManager.Rules.Modlets.Count} modlet(s), {RuleEngine.ActiveCount} of {_modletManager.Rules.Rules.Count} rule(s) active.";
}
