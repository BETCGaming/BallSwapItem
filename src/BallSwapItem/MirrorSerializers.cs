using Mirror;

namespace BallSwapItem;

/// <summary>
/// Registers read/write functions for our network messages.
///
/// Mirror's weaver normally generates these at compile time for every NetworkMessage in the
/// Unity project. A BepInEx mod cannot run the weaver, so without registering them by hand
/// every send fails with "No writer found for ...". Mirror looks the functions up through the
/// public Writer&lt;T&gt;/Reader&lt;T&gt; delegates, so we can simply fill them in.
/// </summary>
internal static class MirrorSerializers
{
    public static void Register()
    {
        // Empty messages: nothing to serialize, but the delegates still have to exist.
        Writer<SwitcherooRequestMessage>.write = static (_, _) => { };
        Reader<SwitcherooRequestMessage>.read = static _ => default;

        Writer<SwitcherooHelloMessage>.write = static (_, _) => { };
        Reader<SwitcherooHelloMessage>.read = static _ => default;

        Writer<SwitcherooArmedMessage>.write =
            static (writer, message) => writer.Write(message.WindUpSeconds);
        Reader<SwitcherooArmedMessage>.read =
            static reader => new SwitcherooArmedMessage { WindUpSeconds = reader.Read<float>() };

        Writer<SwitcherooResultMessage>.write =
            static (writer, message) => writer.Write(message.SwappedCount);
        Reader<SwitcherooResultMessage>.read =
            static reader => new SwitcherooResultMessage { SwappedCount = reader.Read<int>() };

        Plugin.Log.LogInfo("Registered Switcheroo message serializers.");
    }
}
