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
    internal static ConfigEntry<float> SpawnRarity { get; private set; } = null!;
    internal static ConfigEntry<float> SpawnChanceOverride { get; private set; } = null!;
    internal static ConfigEntry<float> HeldPitchDegrees { get; private set; } = null!;
    internal static ConfigEntry<float> HeldYawDegrees { get; private set; } = null!;
    internal static ConfigEntry<float> HeldRollDegrees { get; private set; } = null!;
    internal static ConfigEntry<bool> BlockUnmodded { get; private set; } = null!;
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

        SpawnRarity = Config.Bind(
            "General",
            "SpawnRarity",
            0.35f,
            new ConfigDescription(
                "How common the Switcheroo is, relative to the average item in the same pool. "
                + "1 makes it as common as a typical item; 0.35 makes it noticeably rarer.",
                new AcceptableValueRange<float>(0.05f, 3f)));

        SpawnChanceOverride = Config.Bind(
            "General",
            "SpawnChanceOverride",
            0f,
            new ConfigDescription(
                "Force the Switcheroo to this share of every item pool, ignoring SpawnRarity. "
                + "0.8 means roughly four in five items spawned are Switcheroos. Intended for "
                + "testing; leave at 0 for normal play.",
                new AcceptableValueRange<float>(0f, 0.95f)));

        HeldPitchDegrees = Config.Bind(
            "General",
            "HeldPitchDegrees",
            0f,
            new ConfigDescription(
                "Extra rotation of the device in the player's hands, around the sideways axis.",
                new AcceptableValueRange<float>(-180f, 180f)));

        HeldYawDegrees = Config.Bind(
            "General",
            "HeldYawDegrees",
            0f,
            new ConfigDescription(
                "Extra rotation of the device in the player's hands, around the vertical axis.",
                new AcceptableValueRange<float>(-180f, 180f)));

        HeldRollDegrees = Config.Bind(
            "General",
            "HeldRollDegrees",
            0f,
            new ConfigDescription(
                "Extra rotation of the device in the player's hands, around the forward axis.",
                new AcceptableValueRange<float>(-180f, 180f)));

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
