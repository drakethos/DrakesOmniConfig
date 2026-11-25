using System;
using System.Reflection;
using System.Linq;
using DrakesOmniConfig.Config.Settings;
using DrakesOmniConfig.Engine.Node.Admin;
using HarmonyLib;

namespace DrakesOmniConfig.Tools.ConsoleCommands;

/// <summary>
/// F5 console commands (Server build only), Phase F3. Every command goes through <see cref="AdminChannel"/>, so it works
/// from an admin's own client against a dedicated server and is checked against the server's admin list:
/// <code>
/// omni flag &lt;name&gt; on|off|default
/// omni flags
/// omni configs
/// omni reload
/// </code>
/// The server's reply is printed to the console when it arrives.
/// </summary>
internal static class OmniConfigConsoleCommands
{
    private const string Usage = "omni flag <name> on|off|default · omni flags · omni configs · omni reload";

    private static Terminal? _waiting;
    private static bool _listening;

    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    private static class Terminal_InitTerminal_Patch
    {
        private static bool Prepare() => AccessTools.Method(typeof(Terminal), "InitTerminal") != null;

        private static void Postfix() => Register();
    }

    private static void Register()
    {
        if (!_listening)
        {
            _listening = true;
            AdminChannel.Replied += (ok, message) =>
            {
                if (_waiting == null)
                {
                    return;
                }

                foreach (string line in message.Split('\n'))
                {
                    _waiting.AddString((ok ? "OmniConfig: " : "OmniConfig refused: ") + line);
                }

                _waiting = null;
            };
        }

        AddCommand("omni", "OmniConfig admin: " + Usage, args =>
        {
            string[] words = args.Args.Skip(1).ToArray();
            if (words.Length == 0)
            {
                args.Context.AddString(Usage);
                return;
            }

            if (!AdminChannel.CanAdmin)
            {
                args.Context.AddString("OmniConfig: join a world first (server admins and the host only).");
                return;
            }

            string verb = words[0].ToLowerInvariant();
            string? command = null, arg = "", payload = "";
            switch (verb)
            {
                case "flag" when words.Length == 3:
                    command = "flag.set";
                    arg = words[1];
                    payload = words[2];
                    break;
                case "flags":
                    command = "flag.list";
                    break;
                case "configs":
                    command = "configs.list";
                    break;
                case "reload":
                    command = "reload";
                    break;
            }

            if (command == null)
            {
                args.Context.AddString(Usage);
                return;
            }

            _waiting = args.Context;
            AdminChannel.Send(command, arg, payload);
        });
    }

    /// <summary>
    /// Creates a Terminal.ConsoleCommand through whichever (string, string, ConsoleEvent, ...) constructor the running game
    /// has, the rest at their defaults. The constructor's optional bools change between game versions (the stub refs have
    /// one fewer than live Valheim), so a direct "new" compiles fine and throws MissingMethodException in game.
    /// </summary>
    private static void AddCommand(string name, string description, Terminal.ConsoleEvent action)
    {
        foreach (ConstructorInfo ctor in typeof(Terminal.ConsoleCommand).GetConstructors())
        {
            ParameterInfo[] ps = ctor.GetParameters();
            if (ps.Length < 3 || ps[0].ParameterType != typeof(string) || ps[1].ParameterType != typeof(string) || ps[2].ParameterType != typeof(Terminal.ConsoleEvent))
            {
                continue;
            }

            object?[] values = new object?[ps.Length];
            values[0] = name;
            values[1] = description;
            values[2] = action;
            for (int i = 3; i < ps.Length; i++)
            {
                values[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;
            }

            ctor.Invoke(values);
            return;
        }

        ModLog.Warning($"Console command '{name}' not registered: this game has no Terminal.ConsoleCommand(string, string, ConsoleEvent, ...) constructor.");
    }
}
