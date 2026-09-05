using UnityEngine;

namespace BallSwapItem;

/// <summary>On-screen countdown between using the Switcheroo and the swap landing.</summary>
internal static class SwitcherooUi
{
    private static float countdownEndTime;
    private static GUIStyle? style;

    public static void BeginCountdown(float seconds) => countdownEndTime = Time.time + seconds;

    public static void Draw()
    {
        float remaining = countdownEndTime - Time.time;
        if (remaining <= 0f)
        {
            return;
        }

        style ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 42,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 105f / 255f, 180f / 255f) },
        };

        GUI.Label(
            new Rect(0f, Screen.height * 0.18f, Screen.width, 60f),
            $"SWITCHEROO IN {Mathf.CeilToInt(remaining)}",
            style);
    }
}
