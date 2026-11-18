using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

    public ModletOrchestrator(ModletManager modletManager, FieldPatcher fieldPatcher, ModletAuthority authority)
    {
        _modletManager = modletManager;
        _fieldPatcher = fieldPatcher;
        _authority = authority;
        _authority.ResolvedReceived += OnResolvedReceived;
        _authority.RulesReceived += OnRulesReceived;
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
    private void OnZNetAwake()
    {
        AdminChannel.Register(this);
        if (ZNet.instance != null && ZNet.instance.IsServer())
        {
            ModLog.Info($"Bootstrap: authoritative side (server or hosting) — loading modlets from '{_modletsFolder}'.");
            LoadPublish();
        }
        else
        {
            RuleEngine.SetRules(new NodeRuleSet());
            ModLog.Info("Bootstrap: joined-client side — waiting for the server's resolved overrides.");
        }
    }

    private void LoadPublish()
    {
        _state.Load(_modletsFolder);
        _modletManager.LoadAndResolve(_modletsFolder, _state);
        ApplyAll();
        RuleEngine.SetRules(_modletManager.Rules);
        ModLog.Info($"Rules: {RuleEngine.ActiveCount} active of {_modletManager.Rules.Rules.Count} in {_modletManager.Rules.Modlets.Count} modlet(s).");
        _authority.PublishAll(_modletManager.ResolvedOverrides);
        _authority.PublishRules(_modletManager.Rules);
    }

    private void OnResolvedReceived(IReadOnlyList<ResolvedOverride> resolved)
    {
        ModLog.Info($"Received {resolved.Count} resolved override(s) from the server.");
        _modletManager.AdoptResolved(resolved);
        ApplyAll();
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

            case "rules.put":
                return PutRules(payload);

            case "rules.delete":
                return DeleteRules(arg);

            default:
                return (false, $"Unknown command '{command}'.");
        }
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
