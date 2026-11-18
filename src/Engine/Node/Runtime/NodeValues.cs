using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using DrakesOmniConfig.Engine.Node.Schema;
using HarmonyLib;
using UnityEngine;

namespace DrakesOmniConfig.Engine.Node.Runtime;

/// <summary>Arg parsing shared by every node: numbers, percentages, name patterns and text placeholders.</summary>
internal static class NodeValues
{
    private static readonly Dictionary<string, Regex> PatternCache = new();

    public static string Raw(NodeStep step, string name, string fallback = "") =>
        step.Args.TryGetValue(name, out string? v) && v != null ? v : fallback;

    public static bool Has(NodeStep step, string name) => step.Args.ContainsKey(name);

    public static float Number(NodeStep step, string name, float fallback = 0f) =>
        TryNumber(Raw(step, name), out float v) ? v : fallback;

    public static int Int(NodeStep step, string name, int fallback = 0) => Mathf.RoundToInt(Number(step, name, fallback));

    public static bool Bool(NodeStep step, string name, bool fallback = false)
    {
        string raw = Raw(step, name).Trim();
        if (raw.Length == 0)
        {
            return fallback;
        }

        return raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1" || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryNumber(string raw, out float value)
    {
        raw = raw.Trim().TrimEnd('%').Trim();
        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public static bool IsPercent(string raw) => raw.Trim().EndsWith("%", StringComparison.Ordinal);

    /// <summary>
    /// "+25", "-10%", "=50%", "*1.5", or a bare number (= set). Percentages are of <paramref name="max"/>.
    /// Returns the new value; callers clamp.
    /// </summary>
    public static float ApplyChange(string raw, float current, float max)
    {
        raw = raw.Trim();
        if (raw.Length == 0)
        {
            return current;
        }

        char op = raw[0];
        string rest = op is '+' or '-' or '=' or '*' ? raw.Substring(1) : raw;
        if (op is not ('+' or '-' or '=' or '*'))
        {
            op = '=';
        }

        if (!TryNumber(rest, out float n))
        {
            throw new FormatException($"'{raw}' is not a number change (examples: +25, -10%, =50%, *1.5).");
        }

        float amount = IsPercent(rest) && op != '*' ? max * n / 100f : n;
        return op switch
        {
            '+' => current + amount,
            '-' => current - amount,
            '*' => current * n,
            _ => amount
        };
    }

    /// <summary>Case-insensitive name match. "*" is a wildcard and "|" separates alternatives; an empty pattern matches anything.</summary>
    public static bool Matches(string pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return true;
        }

        if (!PatternCache.TryGetValue(pattern, out Regex? regex))
        {
            var parts = pattern.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = Regex.Escape(parts[i].Trim()).Replace("\\*", ".*");
            }

            regex = new Regex("^(" + string.Join("|", parts) + ")$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            PatternCache[pattern] = regex;
        }

        return regex.IsMatch(value ?? "");
    }

    /// <summary>{item} {itemId} {player} {target} filled from the context; localized when the game's Localization is up.</summary>
    public static string Text(NodeContext ctx, NodeStep step, string name, string fallback = "")
    {
        string raw = Raw(step, name, fallback);
        if (raw.IndexOf('{') >= 0)
        {
            raw = raw
                .Replace("{item}", ctx.Item != null ? ctx.Item.m_shared.m_name : ctx.ItemId)
                .Replace("{itemId}", ctx.ItemId)
                .Replace("{player}", ctx.Player != null ? ctx.Player.GetPlayerName() : "")
                .Replace("{target}", ctx.TargetName);
        }

        return Localize(raw);
    }

    private static Func<string, string>? _localize;
    private static bool _localizeLooked;

    /// <summary>Localization lives in another game assembly this mod doesn't reference, so it is reached by name.</summary>
    public static string Localize(string text)
    {
        if (text.IndexOf('$') < 0)
        {
            return text;
        }

        if (!_localizeLooked)
        {
            _localizeLooked = true;
            var type = AccessTools.TypeByName("Localization");
            var instance = type == null ? null : AccessTools.Property(type, "instance");
            var method = type == null ? null : AccessTools.Method(type, "Localize", new[] { typeof(string) });
            if (instance != null && method != null)
            {
                _localize = s =>
                {
                    object? loc = instance.GetValue(null);
                    return loc == null ? s : (string)method.Invoke(loc, new object[] { s });
                };
            }
        }

        try
        {
            return _localize != null ? _localize(text) : text;
        }
        catch
        {
            return text;
        }
    }

    /// <summary>"Boar(Clone)" -> "Boar".</summary>
    public static string PrefabName(GameObject? go)
    {
        if (go == null)
        {
            return "";
        }

        string name = go.name;
        int clone = name.IndexOf("(Clone)", StringComparison.Ordinal);
        return (clone >= 0 ? name.Substring(0, clone) : name).Trim();
    }

    public static string ItemId(ItemDrop.ItemData? item) =>
        item == null ? "" : item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;
}
