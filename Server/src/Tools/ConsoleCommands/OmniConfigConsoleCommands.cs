using System.Linq;
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

        _ = new Terminal.ConsoleCommand("omni", "OmniConfig admin: " + Usage, args =>
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
}
