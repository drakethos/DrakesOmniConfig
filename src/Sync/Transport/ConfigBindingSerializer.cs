using System;
using System.Collections.Generic;
using System.Linq;
using DrakesOmniConfig.Config.Modlets.Schema;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DrakesOmniConfig.Sync.Transport;

/// <summary>The <c>ConfigBindings</c> sync payload: bindings OmniConfig delivers itself (mods that don't sync the entry).</summary>
internal static class ConfigBindingSerializer
{
    private sealed class Dto
    {
        public string Mod { get; set; } = "";
        public string Section { get; set; } = "";
        public string Key { get; set; } = "";
        public string Operator { get; set; } = "";
        public string? Operand { get; set; }
        public bool IsPercentage { get; set; }
        public string WinningModletId { get; set; } = "";
    }

    private static readonly ISerializer Out = new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
    private static readonly IDeserializer In = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).IgnoreUnmatchedProperties().Build();

    public static string Serialize(IReadOnlyList<ResolvedConfigBinding> bindings) => Out.Serialize(bindings.Select(b => new Dto
    {
        Mod = b.Mod,
        Section = b.Section,
        Key = b.Key,
        Operator = b.Expression.Operator.ToString(),
        Operand = Convert.ToString(b.Expression.Operand, System.Globalization.CultureInfo.InvariantCulture),
        IsPercentage = b.Expression.IsPercentage,
        WinningModletId = b.WinningModletId,
    }).ToList());

    public static List<ResolvedConfigBinding> Deserialize(string text)
    {
        List<Dto>? dtos = In.Deserialize<List<Dto>>(text);
        return dtos?.Select(d => new ResolvedConfigBinding
        {
            Mod = d.Mod,
            Section = d.Section,
            Key = d.Key,
            // operands travel as text: numbers back to double so relative math works the same on both sides
            Expression = new FieldValueExpression
            {
                Operator = (ValueOperator)Enum.Parse(typeof(ValueOperator), d.Operator, ignoreCase: true),
                Operand = double.TryParse(d.Operand, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double n) ? n : (object?)d.Operand,
                IsPercentage = d.IsPercentage,
            },
            WinningModletId = d.WinningModletId,
        }).ToList() ?? new List<ResolvedConfigBinding>();
    }
}
