using System;
using System.Collections.Generic;
using System.Linq;
using DrakesOmniConfig.Config.Modlets.Schema;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DrakesOmniConfig.Sync.Transport;

/// <summary>
/// Turns the server's already-resolved override set into one string for ModletChannel to carry, and
/// back. Deliberately flat DTOs (not Schema.* directly) — same reasoning as ModletLoader's own Raw*
/// types: enums arrive as plain strings here, and a dotted Operand (double or string, same ambiguity
/// FieldValueExpression already has) round-trips through YamlDotNet's own scalar type guessing.
/// </summary>
internal static class ResolvedOverrideSerializer
{
    private sealed class Dto
    {
        public string TypeName { get; set; } = "";
        public string Scope { get; set; } = "";
        public string Kind { get; set; } = "";
        public string? ItemId { get; set; }
        public string? GroupName { get; set; }
        public string? NamePattern { get; set; }
        public string? Category { get; set; }
        public string FieldName { get; set; } = "";
        public string Operator { get; set; } = "";
        public object? Operand { get; set; }
        public bool IsPercentage { get; set; }
        public string WinningModletId { get; set; } = "";
    }

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Writes the list as JSON by hand. JSON is YAML, so <see cref="Deserialize"/> (and older clients) read it unchanged;
    /// YamlDotNet's reflection serializer took ~0.4 s for a 400-clone modlet set on every load.
    /// Operands go out as strings, which is what a YAML round trip made of them anyway.
    /// </summary>
    public static string Serialize(IReadOnlyList<ResolvedOverride> resolved)
    {
        var sb = new System.Text.StringBuilder(resolved.Count * 160);
        sb.Append('[');
        for (int i = 0; i < resolved.Count; i++)
        {
            ResolvedOverride r = resolved[i];
            if (i > 0)
            {
                sb.Append(",\n");
            }

            sb.Append('{');
            Field(sb, "typeName", r.Target.TypeName, first: true);
            Field(sb, "scope", r.Target.Scope.ToString());
            Field(sb, "kind", r.Target.Kind.ToString());
            Field(sb, "itemId", r.Target.ItemId);
            Field(sb, "groupName", r.Target.GroupName);
            Field(sb, "namePattern", r.Target.NamePattern);
            Field(sb, "category", r.Target.Category);
            Field(sb, "fieldName", r.FieldName);
            Field(sb, "operator", r.Expression.Operator.ToString());
            Field(sb, "operand", Operand(r.Expression.Operand));
            sb.Append(",\"isPercentage\":").Append(r.Expression.IsPercentage ? "true" : "false");
            Field(sb, "winningModletId", r.WinningModletId);
            sb.Append('}');
        }

        return sb.Append(']').ToString();
    }

    private static string? Operand(object? value) => value switch
    {
        null => null,
        bool b => b ? "true" : "false",
        double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static void Field(System.Text.StringBuilder sb, string key, string? value, bool first = false)
    {
        if (value == null)
        {
            return; // absent = null after reading, like the YAML serializer's empty value
        }

        if (!first)
        {
            sb.Append(',');
        }

        sb.Append('"').Append(key).Append("\":\"");
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ')
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
    }

    /// <summary>The previous YamlDotNet writer, kept for the round-trip test hook (both must read back the same).</summary>
    internal static string SerializeYaml(IReadOnlyList<ResolvedOverride> resolved)
    {
        List<Dto> dtos = resolved.Select(r => new Dto
        {
            TypeName = r.Target.TypeName,
            Scope = r.Target.Scope.ToString(),
            Kind = r.Target.Kind.ToString(),
            ItemId = r.Target.ItemId,
            GroupName = r.Target.GroupName,
            NamePattern = r.Target.NamePattern,
            Category = r.Target.Category,
            FieldName = r.FieldName,
            Operator = r.Expression.Operator.ToString(),
            Operand = r.Expression.Operand,
            IsPercentage = r.Expression.IsPercentage,
            WinningModletId = r.WinningModletId,
        }).ToList();

        return Serializer.Serialize(dtos);
    }

    public static List<ResolvedOverride> Deserialize(string text)
    {
        List<Dto>? dtos = Deserializer.Deserialize<List<Dto>>(text);
        if (dtos == null)
        {
            return new List<ResolvedOverride>();
        }

        var result = new List<ResolvedOverride>();
        foreach (Dto dto in dtos)
        {
            result.Add(new ResolvedOverride
            {
                Target = new TargetSelector
                {
                    TypeName = dto.TypeName,
                    Scope = (ModletScope)Enum.Parse(typeof(ModletScope), dto.Scope, ignoreCase: true),
                    Kind = (SelectorKind)Enum.Parse(typeof(SelectorKind), dto.Kind, ignoreCase: true),
                    ItemId = dto.ItemId,
                    GroupName = dto.GroupName,
                    NamePattern = dto.NamePattern,
                    Category = dto.Category,
                },
                FieldName = dto.FieldName,
                Expression = new FieldValueExpression
                {
                    Operator = (ValueOperator)Enum.Parse(typeof(ValueOperator), dto.Operator, ignoreCase: true),
                    Operand = dto.Operand,
                    IsPercentage = dto.IsPercentage,
                },
                WinningModletId = dto.WinningModletId,
            });
        }

        return result;
    }
}
