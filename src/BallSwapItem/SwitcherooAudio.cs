using System;
using System.IO;
using System.Runtime.InteropServices;
using FMODUnity;
using UnityEngine;

namespace BallSwapItem;

/// <summary>
/// Plays the payoff sting once a swap has resolved.
///
/// This goes through FMOD rather than a Unity AudioSource. The game runs its audio on FMOD and
/// leaves Unity's own audio system disabled, so AudioClip.Create returns a clip reporting
/// 0 channels at 0 Hz and an AudioSource plays nothing at all. FMOD also decodes MP3 straight
/// from memory, so the sound ships as the original file with no conversion or unpacking.
/// </summary>
internal static class SwitcherooAudio
{
    private const string ResourceName = "BallSwapItem.Assets.bomboclat.mp3";

    private static FMOD.Sound sound;
    private static bool loaded;
    private static bool failed;

    public static void Load() => EnsureSound();

    /// <summary>
    /// The wind-up cue, borrowed from the Orbital Laser the device is built from. Plays on every
    /// client so the whole lobby hears a swap coming, not just whoever used the item.
    /// </summary>
    public static void PlayAnticipation()
    {
        try
        {
            RuntimeManager.PlayOneShot(GameManager.AudioSettings.OrbitalLaserAnticipationEvent);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not play the wind-up cue: {e.Message}");
        }
    }

    public static void PlayPayoff()
    {
        if (!EnsureSound())
        {
            return;
        }

        FMOD.RESULT result = RuntimeManager.CoreSystem.playSound(
            sound,
            default,
            paused: false,
            out FMOD.Channel channel);

        if (result != FMOD.RESULT.OK)
        {
            Plugin.Log.LogWarning($"Could not play the swap sound: {result}");
            return;
        }

        channel.setVolume(Mathf.Clamp01(Plugin.SoundVolume.Value));
    }

    /// <summary>
    /// Creates the FMOD sound on first use. FMOD's core system is not necessarily up when the
    /// plugin starts, so a failed attempt is retried rather than cached as fatal.
    /// </summary>
    private static bool EnsureSound()
    {
        if (loaded)
        {
            return true;
        }

        if (failed)
        {
            return false;
        }

        try
        {
            if (!RuntimeManager.IsInitialized)
            {
                return false;
            }

            byte[]? data = ReadResource();
            if (data is null)
            {
                failed = true;
                return false;
            }

            FMOD.CREATESOUNDEXINFO info = default;
            info.cbsize = Marshal.SizeOf(typeof(FMOD.CREATESOUNDEXINFO));
            info.length = (uint)data.Length;

            FMOD.RESULT result = RuntimeManager.CoreSystem.createSound(
                data,
                FMOD.MODE.OPENMEMORY | FMOD.MODE.CREATESAMPLE | FMOD.MODE.LOOP_OFF,
                ref info,
                out sound);

            if (result != FMOD.RESULT.OK)
            {
                failed = true;
                Plugin.Log.LogWarning($"Could not decode the swap sound, it will not play: {result}");
                return false;
            }

            loaded = true;
            Plugin.Log.LogInfo("Swap sound ready.");
            return true;
        }
        catch (Exception e)
        {
            failed = true;
            Plugin.Log.LogWarning($"Could not prepare the swap sound, it will not play: {e.Message}");
            return false;
        }
    }

    private static byte[]? ReadResource()
    {
        using Stream? stream = typeof(SwitcherooAudio).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            Plugin.Log.LogWarning("Swap sound missing from the plugin; it will not play.");
            return null;
        }

        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
