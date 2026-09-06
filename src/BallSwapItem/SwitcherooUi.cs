using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BallSwapItem;

/// <summary>
/// On-screen countdown between using the Switcheroo and the swap landing.
///
/// Drawn with TextMeshPro on our own canvas rather than IMGUI so it can carry the game's own
/// font, a vertex gradient and an outline. The font is read off a live TMP element in the game's
/// UI, which keeps the countdown matching whatever the game ships even if that changes.
/// </summary>
internal static class SwitcherooUi
{
    /// <summary>The warm off-white the text fades into.</summary>
    private static readonly Color32 GradientBottom = new(0xFF, 0xFC, 0xD4, 0xFF);

    private static TextMeshProUGUI? label;
    private static CanvasGroup? group;
    private static float countdownEndTime;

    public static void BeginCountdown(float seconds) => countdownEndTime = Time.time + seconds;

    /// <summary>Driven every frame by <see cref="ModRunner"/>.</summary>
    public static void Tick()
    {
        float remaining = countdownEndTime - Time.time;

        if (remaining <= 0f)
        {
            if (group != null && group.alpha != 0f)
            {
                group.alpha = 0f;
            }

            return;
        }

        if (!EnsureLabel())
        {
            return;
        }

        group!.alpha = 1f;
        label!.text = $"SWITCHEROO IN {Mathf.CeilToInt(remaining)}";
    }

    private static bool EnsureLabel()
    {
        if (label != null)
        {
            return true;
        }

        TMP_FontAsset? font = FindGameFont();
        if (font == null)
        {
            // The UI may simply not be loaded yet; try again next frame rather than give up.
            return false;
        }

        GameObject root = new("BallSwapItemCountdown");
        UnityEngine.Object.DontDestroyOnLoad(root);

        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the game's HUD, below anything modal.
        canvas.sortingOrder = 500;

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        // Match on height so the text keeps its size on ultrawide displays.
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        group = root.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        GameObject textObject = new("Label");
        textObject.transform.SetParent(root.transform, worldPositionStays: false);

        label = textObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontSize = 76f;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;

        // White through the top of the letters fading to the warm off-white at the base.
        label.enableVertexGradient = true;
        label.colorGradient = new VertexGradient(
            Color.white,
            Color.white,
            GradientBottom,
            GradientBottom);

        // outlineWidth/outlineColor act on this label's own material instance, so the game's
        // shared font material is left untouched.
        label.outlineWidth = 0.2f;
        label.outlineColor = new Color32(0, 0, 0, 255);

        RectTransform rect = label.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -180f);
        rect.sizeDelta = new Vector2(1400f, 120f);

        Plugin.Log.LogInfo($"Countdown label built using the game font '{font.name}'.");
        return true;
    }

    /// <summary>
    /// Borrows the font from a TextMeshPro element the game already has on screen. Picking the
    /// most-used font asset avoids latching onto a one-off decorative element.
    /// </summary>
    private static TMP_FontAsset? FindGameFont()
    {
        TMP_Text[] texts = Resources.FindObjectsOfTypeAll<TMP_Text>();
        if (texts.Length == 0)
        {
            return null;
        }

        return texts
            .Select(text => text.font)
            .Where(font => font != null)
            .GroupBy(font => font)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();
    }
}
