using System;
using DrakesOmniConfig.Config.Settings;

namespace DrakesOmniConfig.Engine.Node.Admin;

/// <summary>What the authoritative side does with an admin request. Implemented by ModletOrchestrator.</summary>
internal interface IAdminHandler
{
    /// <summary>Returns (ok, message for the admin).</summary>
    (bool ok, string message) HandleAdmin(string command, string arg, string payload);
}

/// <summary>
/// Admin requests from a client (an admin running DrakesOmniConfigServer on their own PC) to the server:
/// reload, toggle a modlet or rule, upload a rules file. The server checks the sender is on its admin list
/// (or is the host itself) before doing anything. Commands: reload, modlet.toggle, rule.toggle, rules.put,
/// rules.delete.
/// </summary>
internal static class AdminChannel
{
    private const string RequestRpc = "DrakesOmniConfig_AdminRequest";
    private const string ReplyRpc = "DrakesOmniConfig_AdminReply";
    public const int MaxPayload = 64 * 1024;

    private static IAdminHandler? _handler;

    /// <summary>Raised on the requesting client with the server's answer.</summary>
    public static event Action<bool, string>? Replied;

    public static string LastReply { get; private set; } = "";
    public static bool LastOk { get; private set; }

    /// <summary>Call on every ZNet.Awake: each world load makes a new ZRoutedRpc.</summary>
    public static void Register(IAdminHandler handler)
    {
        _handler = handler;
        if (ZRoutedRpc.instance == null)
        {
            return;
        }

        ZRoutedRpc.instance.Register<string, string, string>(RequestRpc, OnRequest);
        ZRoutedRpc.instance.Register<bool, string>(ReplyRpc, OnReply);
    }

    public static bool CanAdmin => ZNet.instance != null && (ZNet.instance.IsServer() || ZNet.instance.LocalPlayerIsAdminOrHost());

    public static void Send(string command, string arg = "", string payload = "")
    {
        if (ZNet.instance == null)
        {
            return;
        }

        if (payload.Length > MaxPayload)
        {
            Deliver(false, $"That file is too big ({payload.Length / 1024} KB, limit {MaxPayload / 1024} KB).");
            return;
        }

        if (ZNet.instance.IsServer())
        {
            Handle(ZNet.GetUID(), command, arg, payload, local: true);
            return;
        }

        ZNetPeer? server = ZNet.instance.GetServerPeer();
        if (server == null || ZRoutedRpc.instance == null)
        {
            Deliver(false, "Not connected to a server.");
            return;
        }

        ZRoutedRpc.instance.InvokeRoutedRPC(server.m_uid, RequestRpc, command, arg, payload);
    }

    private static void OnRequest(long sender, string command, string arg, string payload)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        Handle(sender, command, arg, payload, local: false);
    }

    private static void Handle(long sender, string command, string arg, string payload, bool local)
    {
        string who = local ? "host" : SenderName(sender);
        (bool ok, string message) result;
        if (!local && !IsAdmin(sender))
        {
            ModLog.Warning($"Refused admin command '{command}' from {who}: not on the server's admin list.");
            result = (false, "You are not an admin on this server.");
        }
        else if (_handler == null)
        {
            result = (false, "The server is not ready yet.");
        }
        else if (payload.Length > MaxPayload)
        {
            result = (false, "That file is too big.");
        }
        else
        {
            try
            {
                result = _handler.HandleAdmin(command, arg, payload);
                ModLog.Info($"Admin command '{command}' {arg} from {who}: {(result.ok ? "ok" : "refused")} — {result.message}");
            }
            catch (Exception ex)
            {
                ModLog.Error($"Admin command '{command}' from {who} failed: {ex.Message}");
                result = (false, "The server hit an error: " + ex.Message);
            }
        }

        if (local)
        {
            Deliver(result.ok, result.message);
        }
        else
        {
            ZRoutedRpc.instance?.InvokeRoutedRPC(sender, ReplyRpc, result.ok, result.message);
        }
    }

    private static void OnReply(long sender, bool ok, string message) => Deliver(ok, message);

    private static void Deliver(bool ok, string message)
    {
        LastOk = ok;
        LastReply = (ok ? "OK: " : "Refused: ") + message;
        Replied?.Invoke(ok, message);
    }

    private static bool IsAdmin(long sender)
    {
        ZNetPeer? peer = ZNet.instance?.GetPeer(sender);
        string host = peer?.m_socket?.GetHostName() ?? "";
        return host.Length > 0 && ZNet.instance!.IsAdmin(host);
    }

    private static string SenderName(long sender)
    {
        ZNetPeer? peer = ZNet.instance?.GetPeer(sender);
        return peer == null ? $"peer {sender}" : $"{peer.m_playerName} ({peer.m_socket?.GetHostName()})";
    }
}
