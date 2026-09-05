using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BallSwapItem;

/// <summary>
/// The plugin's single persistent behaviour: keeps the network handlers registered, draws the
/// countdown, and provides the testing hotkeys.
/// </summary>
internal sealed class ModRunner : MonoBehaviour
{
    /// <summary>Lets the network layer start coroutines from its static message handlers.</summary>
    internal static ModRunner? Instance { get; private set; }

    private void Awake() => Instance = this;

    private void Start() => SwitcherooAudio.Load();

    private void Update()
    {
        SwitcherooNetwork.EnsureHandlers();
        SwitcherooNetwork.TrackHeldItems();
        ModGate.Tick();

        if (!Plugin.DebugHotkeys.Value || Keyboard.current is null)
        {
            return;
        }

        if (Keyboard.current[Key.F9].wasPressedThisFrame)
        {
            GiveSwitcheroo();
        }

        if (Keyboard.current[Key.F10].wasPressedThisFrame)
        {
            ForceSwap();
        }
    }

    private void OnGUI() => SwitcherooUi.Draw();

    private static void GiveSwitcheroo()
    {
        if (!NetworkServer.active)
        {
            Plugin.Log.LogWarning("F9 ignored: only the host can hand out items directly.");
            return;
        }

        PlayerInventory? inventory = GameManager.LocalPlayerInventory;
        if (inventory == null)
        {
            Plugin.Log.LogWarning("F9 ignored: no local player inventory yet.");
            return;
        }

        bool given = inventory.ServerTryAddItem(Switcheroo.Type, remainingUses: 1);
        Plugin.Log.LogInfo(given
            ? "Gave the local player a Switcheroo."
            : "Could not give a Switcheroo (inventory full?).");
    }

    private static void ForceSwap()
    {
        if (!NetworkServer.active)
        {
            Plugin.Log.LogWarning("F10 ignored: only the host can run a swap.");
            return;
        }

        int swapped = SwapService.TrySwap(out string failureReason);
        Plugin.Log.LogInfo(swapped > 0
            ? $"Swapped {swapped} balls."
            : $"Swap did not run: {failureReason}.");
    }
}
