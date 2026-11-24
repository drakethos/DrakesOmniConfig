using System;
using System.Reflection;
using BepInEx.Configuration;

namespace DrakesOmniConfig.Engine.ModConfigs;

/// <summary>
/// Does a mod sync this config entry to players itself? Then OmniConfig only sets it on the server and lets the mod's own
/// sync deliver it. Checked the same way on both sides, by type NAME only: every mod embeds its own copy of ServerSync, so
/// no two of those types are the same type.
/// <list type="bullet">
/// <item>ServerSync: a tag on the entry whose type (or a base type) is <c>OwnConfigEntryBase</c> / <c>SyncedConfigEntry`1</c>,
/// with <c>SynchronizedConfig</c> true when that member exists.</item>
/// <item>Jotunn: a <c>ConfigurationManagerAttributes</c>-like tag with <c>IsAdminOnly</c> true.</item>
/// </list>
/// Pure reflection, no Unity calls.
/// </summary>
internal static class SyncDetection
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static bool IsSelfSynced(ConfigEntryBase entry)
    {
        object[]? tags = entry.Description?.Tags;
        if (tags == null)
        {
            return false;
        }

        foreach (object? tag in tags)
        {
            if (tag == null)
            {
                continue;
            }

            Type type = tag.GetType();
            if (IsServerSyncEntry(type))
            {
                return ReadBool(tag, "SynchronizedConfig") ?? true;
            }

            if (ReadBool(tag, "IsAdminOnly") == true)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsServerSyncEntry(Type? type)
    {
        for (; type != null && type != typeof(object); type = type.BaseType)
        {
            if (type.Name == "OwnConfigEntryBase" || type.Name == "SyncedConfigEntry`1")
            {
                return true;
            }
        }

        return false;
    }

    private static bool? ReadBool(object tag, string name)
    {
        Type type = tag.GetType();
        if (type.GetProperty(name, Members) is { } property && property.PropertyType == typeof(bool) && property.GetIndexParameters().Length == 0)
        {
            return (bool?)property.GetValue(tag);
        }

        if (type.GetField(name, Members) is { } field)
        {
            if (field.FieldType == typeof(bool))
            {
                return (bool)field.GetValue(tag);
            }

            if (field.FieldType == typeof(bool?))
            {
                return (bool?)field.GetValue(tag);
            }
        }

        return null;
    }
}
