using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace DrakesOmniConfig.Config.Flags;

/// <summary>
/// Resolves variables that depend on flags, before <c>{name}</c> substitution (so overrides, configs and rules all get
/// them). A plain value stays as it is; a map picks the first <c>when</c> entry whose flag matches, else <c>default</c>:
/// <code>
/// stackMult:
///   default: 2
///   when:            # first match in listed order wins; "!flag" = flag is off
///     event_weekend: 4
/// carryBonus: 150
/// </code>
/// Pure: no Unity, no IO.
/// </summary>
internal static class ConditionalVars
{
    public static Dictionary<string, object?> Resolve(IReadOnlyDictionary<string, object?> raw, Func<string, bool> isOn)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> variable in raw)
        {
            result[variable.Key] = variable.Value is IDictionary map ? Pick(map, isOn) : variable.Value;
        }

        return result;
    }

    private static object? Pick(IDictionary map, Func<string, bool> isOn)
    {
        object? fallback = null;
        IEnumerable? when = null;
        foreach (DictionaryEntry entry in map)
        {
            string key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "";
            if (key == "default")
            {
                fallback = entry.Value;
            }
            else if (key == "when")
            {
                when = entry.Value as IEnumerable;
            }
        }

        if (when is IDictionary conditions)
        {
            // YamlDotNet keeps a mapping's order in its dictionary, which is the "listed order" the author wrote
            foreach (DictionaryEntry condition in conditions)
            {
                string flag = (Convert.ToString(condition.Key, CultureInfo.InvariantCulture) ?? "").Trim();
                bool negate = flag.StartsWith("!", StringComparison.Ordinal);
                if (negate)
                {
                    flag = flag.Substring(1).Trim();
                }

                if (isOn(flag) != negate)
                {
                    return condition.Value;
                }
            }
        }

        return fallback;
    }
}
