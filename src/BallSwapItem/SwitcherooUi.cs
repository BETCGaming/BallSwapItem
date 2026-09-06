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

    /// <summary>How long the refusal notice stays on screen.</summary>
    private const float DenialSeconds = 1.6f;

    /// <summary>Flashes per second while the refusal notice is showing.</summary>
    private const float DenialFlashHz = 4f;

    private static TextMeshProUGUI? label;
    private static TextMeshProUGUI? denialLabel;
    private static CanvasGroup? group;
    private static float countdownEndTime;
    private static float denialEndTime;
    private static string denialText = string.Empty;

    public static void BeginCountdown(float seconds) => countdownEndTime = Time.time + seconds;

    /// <summary>Shown when a use is refused, explaining why.</summary>
    public static void ShowDenial(string message)
    {
        denialText = message;
        denialEndTime = Time.time + DenialSeconds;
    }

    /// <summary>Driven every frame by <see cref="ModRunner"/>.</summary>
    public static void Tick()
    {
        float countdownRemaining = countdownEndTime - Time.time;
        float denialRemaining = denialEndTime - Time.time;

        if (countdownRemaining <= 0f && denialRemaining <= 0f)
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

        if (countdownRemaining > 0f)
        {
            label!.enabled = true;
            label.text = $"SWITCHEROO IN {Mathf.CeilToInt(countdownRemaining)}";
        }
        else
        {
            label!.enabled = false;
        }

        if (denialRemaining > 0f)
        {
            denialLabel!.enabled = true;
            denialLabel.text = denialText;
            // Square wave rather than a fade, so it reads as a flash rather than a pulse.
            bool on = Mathf.Repeat(denialRemaining * DenialFlashHz, 1f) > 0.5f;
            denialLabel.alpha = on ? 1f : 0.15f;
        }
        else
        {
            denialLabel!.enabled = false;
        }
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

        denialLabel = BuildDenialLabel(root, font);

        Plugin.Log.LogInfo($"Countdown label built using the game font '{font.name}'.");
        return true;
    }

    private static TextMeshProUGUI BuildDenialLabel(GameObject root, TMP_FontAsset font)
    {
        GameObject textObject = new("Denial");
        textObject.transform.SetParent(root.transform, worldPositionStays: false);

        TextMeshProUGUI denial = textObject.AddComponent<TextMeshProUGUI>();
        denial.font = font;
        denial.fontSize = 58f;
        denial.alignment = TextAlignmentOptions.Center;
        denial.textWrappingMode = TextWrappingModes.NoWrap;
        denial.raycastTarget = false;
        denial.color = new Color32(0xFF, 0x3B, 0x30, 0xFF);
        denial.outlineWidth = 0.2f;
        denial.outlineColor = new Color32(0, 0, 0, 255);
        denial.enabled = false;

        RectTransform rect = denial.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -180f);
        rect.sizeDelta = new Vector2(1400f, 120f);

        return denial;
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
