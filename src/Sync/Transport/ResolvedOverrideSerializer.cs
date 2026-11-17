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

    public static string Serialize(IReadOnlyList<ResolvedOverride> resolved)
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
