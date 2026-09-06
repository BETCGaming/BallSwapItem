using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace BallSwapItem;

/// <summary>
/// Performs the ball swap. Server only.
///
/// Nothing here moves a ball. Each player is pointed at a different ball by reassigning two
/// SyncVars — PlayerGolfer.ownBall and GolfBall.owner — which is why a ball caught mid-flight
/// keeps its exact trajectory: only who owns it changes. GolfBall's owner SyncVar hook already
/// refreshes team colour, worldspace icon and cosmetics on every client.
/// </summary>
internal static class SwapService
{
    private static readonly System.Random Rng = new();

    private static readonly Action<GolfBall> UpdateNameTag =
        AccessTools.MethodDelegate<Action<GolfBall>>(
            AccessTools.Method(typeof(GolfBall), "UpdateNameTag"));

    /// <summary>
    /// Swaps every eligible player's ball so that no player keeps their own.
    /// </summary>
    /// <returns>The players whose balls were swapped; empty if the swap could not run.</returns>
    public static List<PlayerGolfer> TrySwap(out string failureReason)
    {
        failureReason = string.Empty;

        if (!NetworkServer.active)
        {
            failureReason = "swap attempted off the server";
            return new List<PlayerGolfer>();
        }

        List<PlayerGolfer> players = CollectEligiblePlayers();
        if (players.Count < 2)
        {
            failureReason = $"only {players.Count} eligible ball(s) in play";
            return new List<PlayerGolfer>();
        }

        GolfBall[] balls = new GolfBall[players.Count];
        for (int i = 0; i < players.Count; i++)
        {
            balls[i] = players[i].NetworkownBall;
        }

        Derange(balls);

        for (int i = 0; i < players.Count; i++)
        {
            players[i].NetworkownBall = balls[i];
            balls[i].Networkowner = players[i];
            UpdateNameTag(balls[i]);
        }

        return players;
    }

    /// <summary>
    /// How many balls a swap would actually move. Safe to call on a client: every check below
    /// reads replicated state, except a ball already in the hole, which only the server tracks —
    /// so a client may count one ball too many and the server stays the authority.
    ///
    /// In the driving range this is always zero, since the game registers no match participants
    /// there at all.
    /// </summary>
    public static int CountEligible() => CollectEligiblePlayers().Count;

    private static List<PlayerGolfer> CollectEligiblePlayers()
    {
        List<PlayerGolfer> eligible = new();

        foreach (PlayerGolfer golfer in CourseManager.MatchParticipants)
        {
            if (golfer == null || golfer.NetworkownBall == null)
            {
                continue;
            }

            // Only players still actively playing the hole. Scored, eliminated and every
            // spectator resolution are excluded.
            if (golfer.MatchResolution != PlayerMatchResolution.None)
            {
                continue;
            }

            GolfBall ball = golfer.NetworkownBall;

            if (ball.isInHole)
            {
                continue;
            }

            // Interrupting the drop-on-head return animation is a known way to strand a ball.
            if (ball.OutOfBoundsReturnState != BallOutOfBoundsReturnState.None)
            {
                continue;
            }

            // A ball still sitting on the tee has not been played yet this hole; leaving it
            // out keeps the swap from robbing someone of a shot they have not taken.
            if (!HasTeedOff(golfer))
            {
                continue;
            }

            eligible.Add(golfer);
        }

        return eligible;
    }

    private static bool HasTeedOff(PlayerGolfer golfer)
    {
        PlayerInfo info = golfer.PlayerInfo;
        if (info == null)
        {
            return false;
        }

        return CourseManager.TryGetPlayerState(info.PlayerId.Guid, out CourseManager.PlayerState state)
            && state.matchStrokes > 0;
    }

    /// <summary>
    /// Shuffles in place until no element sits at its original index, so every player is
    /// guaranteed to end up with someone else's ball. With at most a handful of players,
    /// reshuffling on a fixed point is cheaper and clearer than constructing a derangement.
    /// </summary>
    private static void Derange<T>(T[] items)
    {
        T[] original = (T[])items.Clone();

        for (int attempt = 0; attempt < 100; attempt++)
        {
            for (int i = items.Length - 1; i > 0; i--)
            {
                int j = Rng.Next(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }

            bool hasFixedPoint = false;
            for (int i = 0; i < items.Length; i++)
            {
                if (ReferenceEquals(items[i], original[i]))
                {
                    hasFixedPoint = true;
                    break;
                }
            }

            if (!hasFixedPoint)
            {
                return;
            }
        }

        // Astronomically unlikely; rotating by one is always a derangement.
        Array.Copy(original, items, original.Length);
        T last = items[^1];
        Array.Copy(original, 0, items, 1, original.Length - 1);
        items[0] = last;
    }
}
