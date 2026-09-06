namespace BallSwapItem;

/// <summary>Step-by-step logging for diagnosing a stuck or repeating item use.</summary>
internal static class Trace
{
    public static void Log(string message)
    {
        if (Plugin.VerboseLogging.Value)
        {
            Plugin.Log.LogInfo($"[trace] {message}");
        }
    }
}
