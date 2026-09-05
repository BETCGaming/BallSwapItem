using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace BallSwapItem;

/// <summary>
/// Keeps unmodded players out of a modded lobby.
///
/// The Switcheroo is injected into the item pools, so the host can hand any player an ItemType
/// an unmodded client cannot resolve — it would log errors, show a blank slot and throw on use.
/// Rather than let that happen mid-match, clients that do not announce themselves shortly after
/// connecting are disconnected.
/// </summary>
internal static class ModGate
{
    /// <summary>Generous enough to cover a slow connect, short enough to fail fast.</summary>
    private const float GraceSeconds = 10f;

    private static readonly Dictionary<int, float> FirstSeen = new();
    private static readonly HashSet<int> Modded = new();

    public static void Reset()
    {
        FirstSeen.Clear();
        Modded.Clear();
    }

    public static void MarkModded(NetworkConnectionToClient conn)
    {
        if (conn == null)
        {
            return;
        }

        if (Modded.Add(conn.connectionId))
        {
            Plugin.Log.LogInfo($"Connection {conn.connectionId} has BallSwapItem installed.");
        }
    }

    /// <summary>Polled by <see cref="ModRunner"/> on the server.</summary>
    public static void Tick()
    {
        if (!NetworkServer.active || !Plugin.BlockUnmodded.Value)
        {
            return;
        }

        float now = Time.time;
        List<NetworkConnectionToClient>? toKick = null;

        foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
        {
            if (conn == null || Modded.Contains(conn.connectionId))
            {
                continue;
            }

            // Never gate the host's own connection.
            if (conn == NetworkServer.localConnection)
            {
                continue;
            }

            // The grace period starts at authentication, not at connection. A client cannot
            // announce itself until the game's own authenticator has finished, so counting from
            // first sight would kick players whose handshake is merely slow.
            if (!conn.isAuthenticated)
            {
                continue;
            }

            if (!FirstSeen.TryGetValue(conn.connectionId, out float seen))
            {
                FirstSeen[conn.connectionId] = now;
                continue;
            }

            if (now - seen >= GraceSeconds)
            {
                (toKick ??= new List<NetworkConnectionToClient>()).Add(conn);
            }
        }

        if (toKick is null)
        {
            return;
        }

        foreach (NetworkConnectionToClient conn in toKick)
        {
            Plugin.Log.LogWarning(
                $"Disconnecting connection {conn.connectionId}: BallSwapItem is not installed. "
                + "Set BlockUnmodded to false to allow unmodded players (the Switcheroo will then be withheld).");

            FirstSeen.Remove(conn.connectionId);
            conn.Disconnect();
        }
    }
}
