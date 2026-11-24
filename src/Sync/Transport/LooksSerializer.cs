using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace DrakesOmniConfig.Sync.Transport;

/// <summary>Modlet id -> (look file -> Forge recipe JSON) as one string. The JSON rides inside YAML as plain text.</summary>
internal static class LooksSerializer
{
    private static readonly ISerializer Out = new SerializerBuilder().Build();
    private static readonly IDeserializer In = new DeserializerBuilder().Build();

    public static string Serialize(Dictionary<string, Dictionary<string, string>> looks) => Out.Serialize(looks);

    public static Dictionary<string, Dictionary<string, string>> Deserialize(string payload) =>
        In.Deserialize<Dictionary<string, Dictionary<string, string>>>(payload) ?? new Dictionary<string, Dictionary<string, string>>();
}
