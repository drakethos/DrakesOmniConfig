using System.Collections.Generic;
using System.Linq;
using DrakesOmniConfig.Config.Modlets.Loading;
using DrakesOmniConfig.Config.Modlets.Schema;

namespace DrakesOmniConfig.Config.Modlets.Management;

/// <summary>Owns the current resolved override set. Two entry points, used by exactly one side each — see ModletOrchestrator.</summary>
internal sealed class ModletManager
{
    private readonly ModletLoader _loader;
    private readonly ConflictResolver _resolver;
    private List<Modlet> _loaded = new();
    private List<ResolvedOverride> _resolved = new();

    public ModletManager(ModletLoader loader, ConflictResolver resolver)
    {
        _loader = loader;
        _resolver = resolver;
    }

    public IReadOnlyList<ResolvedOverride> ResolvedOverrides => _resolved;

    /// <summary>Authoritative side only (dedicated server, or singleplayer/hosting client). Reads local files and resolves conflicts.</summary>
    public void LoadAndResolve(string folderPath)
    {
        _loaded = _loader.LoadAll(folderPath);
        _resolved = _resolver.Resolve(_loaded);
    }

    /// <summary>Joined-client side only. Adopts whatever ModletAuthority received from the server — never calls LoadAndResolve.</summary>
    public void AdoptResolved(IReadOnlyList<ResolvedOverride> resolved)
    {
        _resolved = resolved.ToList();
    }
}
