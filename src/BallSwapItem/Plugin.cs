using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace BallSwapItem;

[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    internal static ConfigEntry<bool> Enabled { get; private set; } = null!;
    internal static ConfigEntry<float> WindUpSeconds { get; private set; } = null!;
    internal static ConfigEntry<float> SoundVolume { get; private set; } = null!;
    internal static ConfigEntry<bool> DebugHotkeys { get; private set; } = null!;

    private void Awake()
    {
        Log = Logger;

        Enabled = Config.Bind(
            "General",
            "Enabled",
            true,
            "Enable the Switcheroo item. When off, the item is not registered and never spawns.");

        WindUpSeconds = Config.Bind(
            "General",
            "WindUpSeconds",
            3f,
            new ConfigDescription(
                "Seconds between using the Switcheroo and the swap landing.",
                new AcceptableValueRange<float>(0f, 10f)));

        SoundVolume = Config.Bind(
            "General",
            "SoundVolume",
            0.8f,
            new ConfigDescription(
                "Volume of the sound played once a swap resolves. This plays outside the game's "
                + "FMOD mix, so the in-game volume sliders do not affect it.",
                new AcceptableValueRange<float>(0f, 1f)));

        DebugHotkeys = Config.Bind(
            "Debug",
            "DebugHotkeys",
            false,
            "F9 gives the local player a Switcheroo, F10 forces a swap. Host only, for testing.");

        new Harmony(Id).PatchAll();
        gameObject.AddComponent<ModRunner>();

        Log.LogInfo($"Plugin {Name} v{Version} is loaded!");
    }
}
