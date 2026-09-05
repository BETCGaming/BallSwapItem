using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace BallSwapItem;

/// <summary>Client to server: "I used a Switcheroo, run the swap."</summary>
internal struct SwitcherooRequestMessage : NetworkMessage
{
}

/// <summary>Server to every client: the swap happened, play the payoff.</summary>
internal struct SwitcherooResultMessage : NetworkMessage
{
    public uint InitiatorNetId;
    public int SwappedCount;
}

/// <summary>
/// Mirror's [Command] and [ClientRpc] attributes need the Mirror weaver, which a BepInEx mod
/// cannot run, so the item talks to the host with plain registered messages instead.
/// </summary>
internal static class SwitcherooNetwork
{
    /// <summary>Matches the spirit of the game's own per-connection command rate limiters.</summary>
    private const double MinSecondsBetweenRequests = 1.0;

    private static readonly Dictionary<int, double> LastRequestPerConnection = new();

    private static bool serverHandlerRegistered;
    private static bool clientHandlerRegistered;

    /// <summary>
    /// Mirror drops registered handlers when a server or client shuts down, so handler state is
    /// re-checked every frame rather than hooked onto one particular lifecycle method.
    /// </summary>
    public static void EnsureHandlers()
    {
        if (NetworkServer.active)
        {
            if (!serverHandlerRegistered)
            {
                NetworkServer.RegisterHandler<SwitcherooRequestMessage>(OnServerRequest);
                serverHandlerRegistered = true;
                Plugin.Log.LogInfo("Registered Switcheroo server handler.");
            }
        }
        else if (serverHandlerRegistered)
        {
            serverHandlerRegistered = false;
            LastRequestPerConnection.Clear();
        }

        if (NetworkClient.active)
        {
            if (!clientHandlerRegistered)
            {
                NetworkClient.RegisterHandler<SwitcherooResultMessage>(OnClientResult);
                clientHandlerRegistered = true;
                Plugin.Log.LogInfo("Registered Switcheroo client handler.");
            }
        }
        else if (clientHandlerRegistered)
        {
            clientHandlerRegistered = false;
        }
    }

    /// <summary>Called on the client that used the item, once the wind-up has elapsed.</summary>
    public static void RequestSwap()
    {
        if (!NetworkClient.active)
        {
            Plugin.Log.LogWarning("Switcheroo used while not connected; ignoring.");
            return;
        }

        NetworkClient.Send(new SwitcherooRequestMessage());
    }

    private static void OnServerRequest(NetworkConnectionToClient conn, SwitcherooRequestMessage message)
    {
        if (conn == null)
        {
            return;
        }

        double now = Time.timeAsDouble;
        if (LastRequestPerConnection.TryGetValue(conn.connectionId, out double last)
            && now - last < MinSecondsBetweenRequests)
        {
            Plugin.Log.LogWarning($"Ignoring rapid Switcheroo request from connection {conn.connectionId}.");
            return;
        }

        LastRequestPerConnection[conn.connectionId] = now;

        if (!SenderHoldsSwitcheroo(conn))
        {
            Plugin.Log.LogWarning($"Ignoring Switcheroo request from connection {conn.connectionId}: no Switcheroo in their inventory.");
            return;
        }

        int swapped = SwapService.TrySwap(out string failureReason);
        if (swapped == 0)
        {
            Plugin.Log.LogInfo($"Switcheroo used but no swap ran: {failureReason}.");
            return;
        }

        Plugin.Log.LogInfo($"Switcheroo swapped {swapped} balls.");

        NetworkServer.SendToAll(new SwitcherooResultMessage
        {
            InitiatorNetId = conn.identity == null ? 0u : conn.identity.netId,
            SwappedCount = swapped,
        });
    }

    /// <summary>
    /// The using client sends this request before decrementing the item, so the server's own
    /// authoritative slot list still shows the Switcheroo when the request lands.
    /// </summary>
    private static bool SenderHoldsSwitcheroo(NetworkConnectionToClient conn)
    {
        if (conn.identity == null)
        {
            return false;
        }

        PlayerInventory? inventory = conn.identity.GetComponentInChildren<PlayerInventory>();
        if (inventory == null)
        {
            return false;
        }

        foreach (InventorySlot slot in inventory.slots)
        {
            if (slot.itemType == Switcheroo.Type && slot.remainingUses > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void OnClientResult(SwitcherooResultMessage message)
    {
        Plugin.Log.LogInfo($"Switcheroo swapped {message.SwappedCount} balls.");
        SwitcherooAudio.PlayPayoff();
    }
}
