using System.Collections.Generic;
using System.Linq;
using DrakesOmniConfig.Config.Modlets.Loading;
using DrakesOmniConfig.Config.Modlets.Schema;
using DrakesOmniConfig.Engine.Node.Admin;
using DrakesOmniConfig.Engine.Node.Loading;
using DrakesOmniConfig.Engine.Node.Schema;

namespace DrakesOmniConfig.Config.Modlets.Management;

/// <summary>Owns the current resolved override set and node rule set. Two entry points, used by exactly one side each — see ModletOrchestrator.</summary>
internal sealed class ModletManager
{
    private readonly ModletLoader _loader;
    private readonly ConflictResolver _resolver;
    private readonly NodeLoader _nodeLoader = new();
    private List<Modlet> _loaded = new();
    private List<ResolvedOverride> _resolved = new();

    public ModletManager(ModletLoader loader, ConflictResolver resolver)
    {
        _loader = loader;
        _resolver = resolver;
    }

    public IReadOnlyList<ResolvedOverride> ResolvedOverrides => _resolved;
    public NodeRuleSet Rules { get; private set; } = new();
    public NodeLoader NodeLoader => _nodeLoader;

    /// <summary>Authoritative side only (dedicated server, or singleplayer/hosting client). Reads local files and resolves conflicts.</summary>
    public void LoadAndResolve(string folderPath, NodeState state)
    {
        _loaded = _loader.LoadAll(folderPath);
        _resolved = _resolver.Resolve(_loaded.Where(m => !state.DisabledModlets.Contains(m.Manifest.Id)).ToList());

        var set = new NodeRuleSet();
        foreach (Modlet modlet in _loaded)
        {
            string id = modlet.Manifest.Id;
            bool modletOff = state.DisabledModlets.Contains(id);
            List<NodeRule> rules = _nodeLoader.LoadModlet(modlet, id);
            foreach (NodeRule rule in rules)
            {
                rule.DisabledByAdmin = modletOff || state.DisabledRules.Contains(rule.Key);
            }

            set.Rules.AddRange(rules);
            set.Modlets.Add(new NodeModletInfo
            {
                Id = id,
                DisplayName = modlet.Manifest.DisplayName.Length > 0 ? modlet.Manifest.DisplayName : id,
                Enabled = modlet.Manifest.Enabled,
                DisabledByAdmin = modletOff,
                OverrideCount = modlet.Overrides.Sum(o => o.Fields.Count),
                RuleCount = rules.Count
            });
        }

        Rules = set;
    }

    /// <summary>Joined-client side only. Adopts whatever ModletAuthority received from the server — never calls LoadAndResolve.</summary>
    public void AdoptResolved(IReadOnlyList<ResolvedOverride> resolved)
    {
        _resolved = resolved.ToList();
    }

    public void AdoptRules(NodeRuleSet rules)
    {
        Rules = rules;
    }
}
