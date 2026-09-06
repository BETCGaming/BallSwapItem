using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace BallSwapItem;

/// <summary>Where a use that has been sent to the host stands, from the using client's side.</summary>
internal enum SwapRequestState
{
    None,
    Waiting,
    Accepted,
    Denied,
}

/// <summary>Client to server: "I used a Switcheroo."</summary>
internal struct SwitcherooRequestMessage : NetworkMessage
{
    /// <summary>Echoed back in the reply, so a late answer cannot be read as the next use's.</summary>
    public uint Token;
}

/// <summary>Client to server on connect: "I have the mod installed."</summary>
internal struct SwitcherooHelloMessage : NetworkMessage
{
}

/// <summary>Server to every client: a swap is coming, start the countdown.</summary>
internal struct SwitcherooArmedMessage : NetworkMessage
{
    public float WindUpSeconds;

    /// <summary>Who set it off, so every player's countdown can say so. Empty if unknown.</summary>
    public string UserName;
}

/// <summary>Why the host turned a use down, so the client can say which rule stopped it.</summary>
internal enum SwitcherooDenialReason : byte
{
    None,
    SwapInProgress,
    AlreadyUsedThisRound,
    NotHolding,
    TooSoon,
    NotEnoughBalls,
}

/// <summary>
/// Server to the one client that asked: whether the use is going ahead.
///
/// The item is not spent until this says yes. Without it a use that the host turns down still
/// cost the player their Switcheroo, with nothing on screen to say why.
/// </summary>
internal struct SwitcherooUseReplyMessage : NetworkMessage
{
    public bool Accepted;
    public byte Reason;
    public uint Token;
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

    /// <summary>
    /// How long after the wind-up a client keeps treating a swap as running. The host sends no
    /// result when a swap ends up swapping nothing, so the busy state expires on its own rather
    /// than relying on a message that may never come.
    /// </summary>
    private const float BusyGraceSeconds = 1.5f;

    /// <summary>How long a client waits for the host's verdict before giving up on a use.</summary>
    public const float ReplyTimeoutSeconds = 2f;

    /// <summary>Set on clients from the host. False whenever the rule is off.</summary>
    public static bool LockedThisRound { get; private set; }

    /// <summary>
    /// True on every client from the moment a swap is armed until it lands. A second Switcheroo
    /// used inside that window cannot do anything — the host refuses it — so the use is stopped
    /// here, before it costs anyone their item.
    /// </summary>
    public static bool SwapInProgress => Time.timeAsDouble < clientBusyUntil;

    /// <summary>Where a use that has been sent to the host currently stands.</summary>
    public static SwapRequestState RequestState { get; private set; } = SwapRequestState.None;

    /// <summary>Set alongside a denied <see cref="RequestState"/>, ready to show to the player.</summary>
    public static string DenialText { get; private set; } = "SWAP UNAVAILABLE";

    private static double clientBusyUntil;
    private static uint pendingToken;

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
                NetworkClient.RegisterHandler<SwitcherooUseReplyMessage>(OnClientUseReply);
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
            clientBusyUntil = 0;
            RequestState = SwapRequestState.None;
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

    /// <summary>
    /// Called on the client that used the item. The caller then waits on
    /// <see cref="RequestState"/> and only spends the Switcheroo once the host has accepted.
    /// </summary>
    public static void RequestSwap()
    {
        if (!NetworkClient.active)
        {
            Plugin.Log.LogWarning("Switcheroo used while not connected; ignoring.");
            RequestState = SwapRequestState.Denied;
            DenialText = "NOT CONNECTED";
            return;
        }

        RequestState = SwapRequestState.Waiting;
        pendingToken++;
        NetworkClient.Send(new SwitcherooRequestMessage { Token = pendingToken });
    }

    /// <summary>Ends the wait when the host says nothing at all, so the item is kept.</summary>
    public static void TimeOutRequest()
    {
        RequestState = SwapRequestState.Denied;
        DenialText = "NO RESPONSE";
    }

    private static void OnClientUseReply(SwitcherooUseReplyMessage message)
    {
        // An answer to a use we have already given up on, arriving while a newer one is in
        // flight, must not be mistaken for the newer one's verdict.
        if (message.Token != pendingToken || RequestState != SwapRequestState.Waiting)
        {
            return;
        }

        if (message.Accepted)
        {
            RequestState = SwapRequestState.Accepted;
            return;
        }

        SwitcherooDenialReason reason = (SwitcherooDenialReason)message.Reason;
        RequestState = SwapRequestState.Denied;
        DenialText = reason switch
        {
            SwitcherooDenialReason.SwapInProgress => "SWAP IN PROGRESS",
            SwitcherooDenialReason.AlreadyUsedThisRound => "ONCE PER ROUND",
            SwitcherooDenialReason.NotEnoughBalls => "NO BALLS TO SWAP",
            SwitcherooDenialReason.NotHolding => "NO SWITCHEROO",
            SwitcherooDenialReason.TooSoon => "TOO SOON",
            _ => "SWAP UNAVAILABLE",
        };

        Trace.Log($"client: use denied by the host ({reason})");
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
            Deny(conn, message.Token, SwitcherooDenialReason.TooSoon);
            return;
        }

        LastRequestPerConnection[conn.connectionId] = now;

        if (swapPending)
        {
            Plugin.Log.LogInfo("Switcheroo request ignored: a swap is already counting down.");
            Deny(conn, message.Token, SwitcherooDenialReason.SwapInProgress);
            return;
        }

        // The client checks this too and refuses without spending the item; this is the
        // authoritative backstop.
        if (serverLocked)
        {
            Plugin.Log.LogInfo("Switcheroo request ignored: already used this round.");
            Deny(conn, message.Token, SwitcherooDenialReason.AlreadyUsedThisRound);
            return;
        }

        if (!SenderHoldsSwitcheroo(conn))
        {
            Plugin.Log.LogWarning(
                $"Ignoring Switcheroo request from connection {conn.connectionId}: no Switcheroo in their inventory.");
            Deny(conn, message.Token, SwitcherooDenialReason.NotHolding);
            return;
        }

        // Decided here rather than once the countdown coroutine is running: acceptance is what
        // spends the requester's item, so everything that can refuse has to have refused by now.
        int eligible = SwapService.CountEligible();
        if (eligible < 2)
        {
            Plugin.Log.LogInfo($"Switcheroo request refused: only {eligible} eligible ball(s) in play.");
            Deny(conn, message.Token, SwitcherooDenialReason.NotEnoughBalls);
            return;
        }

        PlayerInfo? initiator = conn.identity == null ? null : conn.identity.GetComponent<PlayerInfo>();
        Trace.Log($"server: request accepted from connection {conn.connectionId}");
        conn.Send(new SwitcherooUseReplyMessage
        {
            Accepted = true,
            Reason = (byte)SwitcherooDenialReason.None,
            Token = message.Token,
        });
        ModRunner.Instance?.StartCoroutine(RunSwap(initiator));
    }

    private static void Deny(NetworkConnectionToClient conn, uint token, SwitcherooDenialReason reason)
    {
        conn.Send(new SwitcherooUseReplyMessage { Accepted = false, Reason = (byte)reason, Token = token });
    }

    private static IEnumerator RunSwap(PlayerInfo? initiator)
    {
        // Eligibility was settled in OnServerRequest, before the use was accepted, so a countdown
        // never starts for a swap that cannot happen.
        swapPending = true;

        float windUp = Mathf.Max(0f, Plugin.WindUpSeconds.Value);
        NetworkServer.SendToAll(new SwitcherooArmedMessage
        {
            WindUpSeconds = windUp,
            UserName = NameOf(initiator),
        });

        Trace.Log($"server: armed, waiting {windUp:0.0}s");
        yield return new WaitForSeconds(windUp);
        Trace.Log("server: wind-up elapsed");

        // The server can stop being the server mid-countdown, e.g. the host leaves.
        if (!NetworkServer.active)
        {
            Plugin.Log.LogWarning("Swap abandoned: no longer the server when the wind-up elapsed.");
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

            // Host-authoritative and set in the match setup. GetValueAsBool falls back to the
            // rule's default when there is no setup screen, which is off.
            if (MatchSetupRules.GetValueAsBool(Switcheroo.OncePerHoleRule))
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
    /// The name to show for whoever set a swap off. Taken without rich text: a player's name is
    /// theirs to choose, and our countdown label would otherwise render their markup.
    /// </summary>
    private static string NameOf(PlayerInfo? player)
    {
        if (player == null)
        {
            return string.Empty;
        }

        PlayerId? id = player.PlayerId;
        return id == null ? string.Empty : id.PlayerNameNoRichText ?? string.Empty;
    }

    /// <summary>
    /// Reuses the game's own item-hit feed line, one per player caught in the swap, which is how
    /// the game reports an item affecting several people at once. The line is posted by the
    /// server and the game shows it on every client, so this is what tells the rest of the lobby
    /// who used the Switcheroo.
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
        clientBusyUntil = Time.timeAsDouble + message.WindUpSeconds + BusyGraceSeconds;
        SwitcherooUi.BeginCountdown(message.WindUpSeconds, message.UserName);
        SwitcherooAudio.PlayAnticipation();
    }

    private static void OnClientResult(SwitcherooResultMessage message)
    {
        clientBusyUntil = 0;
        Plugin.Log.LogInfo($"Switcheroo swapped {message.SwappedCount} balls.");
        SwitcherooAudio.PlayPayoff();
    }
}
