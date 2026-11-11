using System.Collections.Generic;
using DrakesOmniConfig.Engine.Fields.Accessors;

namespace DrakesOmniConfig.Engine.Fields.Registry;

/// <summary>
/// Resolves (TypeName, FieldName) -> IFieldAccessor. Reflected is the automatic fallback; Virtual
/// accessors are registered explicitly, one at a time, for the handful of fields that need one
/// (see Accessors.Virtual). A modlet's YAML never says which kind it's touching — this is the only
/// place that decides.
/// </summary>
internal sealed class FieldRegistry
{
    private readonly Dictionary<string, IFieldAccessor> _virtual = new();

    // TODO(you): call once at startup, e.g. RegisterVirtual("Character", "FallDamageMultiplier", new FallDamageAccessor()).
    public void RegisterVirtual(string typeName, string fieldName, IFieldAccessor accessor)
    {
    }

    // TODO(you): check _virtual first (key likely "TypeName.FieldName"); fall back to building/caching
    // a ReflectedFieldAccessor for anything not explicitly registered.
    public IFieldAccessor Resolve(string typeName, string fieldName)
    {
        return null!;
    }
}
