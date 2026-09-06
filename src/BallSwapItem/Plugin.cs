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
    internal static ConfigEntry<float> SpawnChance { get; private set; } = null!;
    internal static ConfigEntry<bool> OneSwitchPerRound { get; private set; } = null!;
    internal static ConfigEntry<bool> BlockUnmodded { get; private set; } = null!;
    internal static ConfigEntry<float> SoundVolume { get; private set; } = null!;
    internal static ConfigEntry<float> DenialVolume { get; private set; } = null!;
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

        SpawnChance = Config.Bind(
            "General",
            "SpawnChance",
            0.07f,
            new ConfigDescription(
                "Chance that any item the game hands out is a Switcheroo, from item crates and "
                + "catch-up hand-outs alike. Host setting; item spawning is server side.",
                new AcceptableValueRange<float>(0f, 0.95f)));

        OneSwitchPerRound = Config.Bind(
            "Host",
            "OneSwitchPerRound",
            false,
            "Allow only one Switcheroo swap per hole. Further attempts are refused without "
            + "consuming the item. Host setting; clients follow whatever the host runs.");

        BlockUnmodded = Config.Bind(
            "Host",
            "BlockUnmodded",
            true,
            "Disconnect players who do not have this mod installed. They cannot handle the "
            + "Switcheroo if the host hands them one. Host setting; ignored on clients.");

        SoundVolume = Config.Bind(
            "General",
            "SoundVolume",
            0.8f,
            new ConfigDescription(
                "Volume of the sound played once a swap resolves. This plays outside the game's "
                + "FMOD mix, so the in-game volume sliders do not affect it.",
                new AcceptableValueRange<float>(0f, 1f)));

        DenialVolume = Config.Bind(
            "General",
            "DenialVolume",
            0.25f,
            new ConfigDescription(
                "Volume of the sound played when a Switcheroo use is refused because the swap "
                + "has already been used this round.",
                new AcceptableValueRange<float>(0f, 1f)));

        DebugHotkeys = Config.Bind(
            "Debug",
            "DebugHotkeys",
            false,
            "F9 gives the local player a Switcheroo, F10 forces a swap. Host only, for testing.");

        MirrorSerializers.Register();
        SwitcherooLocalization.Initialize();
        new Harmony(Id).PatchAll();
        gameObject.AddComponent<ModRunner>();

        Log.LogInfo($"Plugin {Name} v{Version} is loaded!");
    }
}
