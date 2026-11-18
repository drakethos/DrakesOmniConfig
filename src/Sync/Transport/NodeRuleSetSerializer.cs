using DrakesOmniConfig.Engine.Node.Schema;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DrakesOmniConfig.Sync.Transport;

/// <summary>The server's rule set (every rule, disabled ones included, plus the modlet list the admin panel shows) as one string.</summary>
internal static class NodeRuleSetSerializer
{
    private static readonly ISerializer Out = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer In = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static string Serialize(NodeRuleSet set) => Out.Serialize(set);

    public static NodeRuleSet Deserialize(string payload) => In.Deserialize<NodeRuleSet>(payload) ?? new NodeRuleSet();
}
