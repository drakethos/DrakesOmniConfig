using System;
using System.IO;
using BepInEx;
using DrakesOmniConfig.Config.Settings;
using DrakesOmniConfig.Engine.Node.Runtime;
using HarmonyLib;

namespace DrakesOmniConfig.Tools.HtmlHelper;

/// <summary>Writes NodeCatalog.json for the Rules editor once the game's item and status effect lists exist.</summary>
internal static class NodeCatalogWriter
{
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    private static class ZNetScene_Awake_CatalogPatch
    {
        private static void Postfix()
        {
            try
            {
                NodeCatalog.Write(Path.Combine(Paths.ConfigPath, "DrakesOmniConfig", "NodeCatalog.json"));
            }
            catch (Exception ex)
            {
                ModLog.Error($"Could not write NodeCatalog.json: {ex.Message}");
            }
        }
    }
}
