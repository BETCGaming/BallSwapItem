using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace BallSwapItem;

/// <summary>Client to server: "I used a Switcheroo."</summary>
internal struct SwitcherooRequestMessage : NetworkMessage
{
}

/// <summary>Client to server on connect: "I have the mod installed."</summary>
internal struct SwitcherooHelloMessage : NetworkMessage
{
}

/// <summary>Server to every client: a swap is coming, start the countdown.</summary>
internal struct SwitcherooArmedMessage : NetworkMessage
{
    public float WindUpSeconds;
}

/// <summary>Server to every client: the swap landed, play the payoff.</summary>
internal struct SwitcherooResultMessage : NetworkMessage
{
    public int SwappedCount;
}

/// <summary>
/// Mirror's [Command] and [ClientRpc] attributes need the Mirror weaver, which a BepInEx mod
/// cannot run, so the item talks to the host with plain registered messages instead.
///
/// The wind-up is timed on the server rather than on the using client, so every player gets the
/// same countdown and the same warning rather than only the person who pressed the button.
/// </summary>
internal static class SwitcherooNetwork
{
    /// <summary>Matches the spirit of the game's own per-connection command rate limiters.</summary>
    private const double MinSecondsBetweenRequests = 1.0;

    private static readonly Dictionary<int, double> LastRequestPerConnection = new();

    private static bool serverHandlerRegistered;
    private static bool clientHandlerRegistered;
    private static bool helloSent;
    private static bool swapPending;

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
                NetworkServer.RegisterHandler<SwitcherooHelloMessage>(OnServerHello);
                serverHandlerRegistered = true;
                Plugin.Log.LogInfo("Registered Switcheroo server handlers.");
            }
        }
        else if (serverHandlerRegistered)
        {
            serverHandlerRegistered = false;
            swapPending = false;
            LastRequestPerConnection.Clear();
            LastHeldSwitcheroo.Clear();
            ModGate.Reset();
        }

        if (NetworkClient.active)
        {
            if (!clientHandlerRegistered)
            {
                NetworkClient.RegisterHandler<SwitcherooArmedMessage>(OnClientArmed);
                NetworkClient.RegisterHandler<SwitcherooResultMessage>(OnClientResult);
                clientHandlerRegistered = true;
                Plugin.Log.LogInfo("Registered Switcheroo client handlers.");
            }

            // Announce ourselves so the host knows this client has the mod.
            if (!helloSent && NetworkClient.isConnected)
            {
                NetworkClient.Send(new SwitcherooHelloMessage());
                helloSent = true;
            }
        }
        else
        {
            clientHandlerRegistered = false;
            helloSent = false;
        }
    }

    /// <summary>Called on the client that used the item.</summary>
    public static void RequestSwap()
    {
        if (!NetworkClient.active)
        {
            Plugin.Log.LogWarning("Switcheroo used while not connected; ignoring.");
            return;
        }

        NetworkClient.Send(new SwitcherooRequestMessage());
    }

    private static void OnServerHello(NetworkConnectionToClient conn, SwitcherooHelloMessage message)
        => ModGate.MarkModded(conn);

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

        if (swapPending)
        {
            Plugin.Log.LogInfo("Switcheroo request ignored: a swap is already counting down.");
            return;
        }

        if (!SenderHoldsSwitcheroo(conn))
        {
            Plugin.Log.LogWarning(
                $"Ignoring Switcheroo request from connection {conn.connectionId}: no Switcheroo in their inventory.");
            return;
        }

        ModRunner.Instance?.StartCoroutine(RunSwap());
    }

    private static IEnumerator RunSwap()
    {
        swapPending = true;

        float windUp = Mathf.Max(0f, Plugin.WindUpSeconds.Value);
        NetworkServer.SendToAll(new SwitcherooArmedMessage { WindUpSeconds = windUp });

        yield return new WaitForSeconds(windUp);

        // The server can stop being the server mid-countdown, e.g. the host leaves.
        if (!NetworkServer.active)
        {
            swapPending = false;
            yield break;
        }

        int swapped = SwapService.TrySwap(out string failureReason);
        if (swapped == 0)
        {
            Plugin.Log.LogInfo($"Switcheroo used but no swap ran: {failureReason}.");
        }
        else
        {
            Plugin.Log.LogInfo($"Switcheroo swapped {swapped} balls.");
            NetworkServer.SendToAll(new SwitcherooResultMessage { SwappedCount = swapped });
        }

        swapPending = false;
    }

    /// <summary>
    /// How long after a player last held a Switcheroo their request is still honoured. In host
    /// mode Mirror queues local-connection messages and delivers them on a later network update,
    /// by which time the item has already been consumed, so an exact "holds it right now" check
    /// rejects the host's own perfectly legitimate use.
    /// </summary>
    private const float HeldMemorySeconds = 5f;

    private static readonly Dictionary<int, float> LastHeldSwitcheroo = new();

    /// <summary>Polled on the server so recent holders are known before their request lands.</summary>
    public static void TrackHeldItems()
    {
        if (!NetworkServer.active)
        {
            return;
        }

        float now = Time.time;
        foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
        {
            if (conn != null && HoldsSwitcheroo(conn))
            {
                LastHeldSwitcheroo[conn.connectionId] = now;
            }
        }
    }

    private static bool SenderHoldsSwitcheroo(NetworkConnectionToClient conn)
    {
        if (HoldsSwitcheroo(conn))
        {
            return true;
        }

        return LastHeldSwitcheroo.TryGetValue(conn.connectionId, out float last)
            && Time.time - last <= HeldMemorySeconds;
    }

    private static bool HoldsSwitcheroo(NetworkConnectionToClient conn)
    {
        if (conn.identity == null)
        {
            return false;
        }

        PlayerInventory? inventory = conn.identity.GetComponent<PlayerInfo>()?.Inventory;
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

    private static void OnClientArmed(SwitcherooArmedMessage message)
    {
        SwitcherooUi.BeginCountdown(message.WindUpSeconds);
        SwitcherooAudio.PlayAnticipation();
    }

    private static void OnClientResult(SwitcherooResultMessage message)
    {
        Plugin.Log.LogInfo($"Switcheroo swapped {message.SwappedCount} balls.");
        SwitcherooAudio.PlayPayoff();
    }
}
