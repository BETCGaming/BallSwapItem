using System;
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
/// Server to clients: whether the Switcheroo is spent for this hole. The rule lives on the
/// host, so clients simply obey this rather than consulting their own config, which keeps a
/// mismatched client setting from letting someone through.
/// </summary>
internal struct SwitcherooLockMessage : NetworkMessage
{
    public bool Locked;
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

    /// <summary>Set on clients from the host. False whenever the rule is off.</summary>
    public static bool LockedThisRound { get; private set; }

    private static bool serverLocked;
    private static bool holeHookInstalled;
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

                // Registered without requiring authentication on purpose. Mirror disconnects a
                // connection outright if it receives an auth-required message before the game's
                // own authenticator has finished, so a hello that arrives a frame early must not
                // be able to kick the very players who have the mod installed.
                NetworkServer.RegisterHandler<SwitcherooHelloMessage>(OnServerHello, requireAuthentication: false);
                serverHandlerRegistered = true;
                Plugin.Log.LogInfo("Registered Switcheroo server handlers.");
            }
        }
        else if (serverHandlerRegistered)
        {
            serverHandlerRegistered = false;
            swapPending = false;
            serverLocked = false;
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
                NetworkClient.RegisterHandler<SwitcherooLockMessage>(OnClientLock);
                clientHandlerRegistered = true;
                Switcheroo.EnsureNetworkPrefabRegistered();
                Plugin.Log.LogInfo("Registered Switcheroo client handlers.");
            }

            // Announce ourselves so the host knows this client has the mod, but only once the
            // game's own authentication has completed.
            if (!helloSent && NetworkClient.isConnected && NetworkClient.connection is { isAuthenticated: true })
            {
                NetworkClient.Send(new SwitcherooHelloMessage());
                helloSent = true;
            }
        }
        else
        {
            clientHandlerRegistered = false;
            helloSent = false;
            LockedThisRound = false;
        }

        if (!holeHookInstalled)
        {
            CourseManager.CurrentHoleGlobalIndexChanged += OnHoleChanged;
            holeHookInstalled = true;
        }
    }

    /// <summary>A new hole is a new round, so the once-per-round allowance comes back.</summary>
    private static void OnHoleChanged()
    {
        LockedThisRound = false;

        if (!NetworkServer.active)
        {
            return;
        }

        serverLocked = false;
        NetworkServer.SendToAll(new SwitcherooLockMessage { Locked = false });
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
    {
        ModGate.MarkModded(conn);

        // Bring a joining player up to date; they may have arrived after the swap was spent.
        conn.Send(new SwitcherooLockMessage { Locked = serverLocked });
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

        if (swapPending)
        {
            Plugin.Log.LogInfo("Switcheroo request ignored: a swap is already counting down.");
            return;
        }

        // The client checks this too and refuses without spending the item; this is the
        // authoritative backstop.
        if (serverLocked)
        {
            Plugin.Log.LogInfo("Switcheroo request ignored: already used this round.");
            return;
        }

        if (!SenderHoldsSwitcheroo(conn))
        {
            Plugin.Log.LogWarning(
                $"Ignoring Switcheroo request from connection {conn.connectionId}: no Switcheroo in their inventory.");
            return;
        }

        PlayerInfo? initiator = conn.identity == null ? null : conn.identity.GetComponent<PlayerInfo>();
        ModRunner.Instance?.StartCoroutine(RunSwap(initiator));
    }

    private static IEnumerator RunSwap(PlayerInfo? initiator)
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

        List<PlayerGolfer> swapped = SwapService.TrySwap(out string failureReason);
        if (swapped.Count == 0)
        {
            Plugin.Log.LogInfo($"Switcheroo used but no swap ran: {failureReason}.");
        }
        else
        {
            Plugin.Log.LogInfo($"Switcheroo swapped {swapped.Count} balls.");
            NetworkServer.SendToAll(new SwitcherooResultMessage { SwappedCount = swapped.Count });
            AnnounceInFeed(initiator, swapped);

            if (Plugin.OneSwitchPerRound.Value)
            {
                serverLocked = true;
                NetworkServer.SendToAll(new SwitcherooLockMessage { Locked = true });
            }
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

    /// <summary>
    /// Reuses the game's own item-hit feed line, one per player caught in the swap, which is how
    /// the game reports an item affecting several people at once.
    /// </summary>
    private static void AnnounceInFeed(PlayerInfo? initiator, List<PlayerGolfer> swapped)
    {
        if (initiator == null)
        {
            return;
        }

        try
        {
            foreach (PlayerGolfer golfer in swapped)
            {
                PlayerInfo affected = golfer.PlayerInfo;
                if (affected != null && affected != initiator)
                {
                    InfoFeed.ShowItemHitMessage(initiator, affected, Switcheroo.Type);
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not post the swap to the info feed: {e.Message}");
        }
    }

    private static void OnClientLock(SwitcherooLockMessage message)
    {
        LockedThisRound = message.Locked;
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
