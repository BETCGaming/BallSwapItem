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
        Writer<SwitcherooRequestMessage>.write =
            static (writer, message) => writer.Write(message.Token);
        Reader<SwitcherooRequestMessage>.read =
            static reader => new SwitcherooRequestMessage { Token = reader.Read<uint>() };

        // Empty message: nothing to serialize, but the delegates still have to exist.
        Writer<SwitcherooHelloMessage>.write = static (_, _) => { };
        Reader<SwitcherooHelloMessage>.read = static _ => default;

        Writer<SwitcherooArmedMessage>.write = static (writer, message) =>
        {
            writer.Write(message.WindUpSeconds);
            writer.Write(message.UserName);
        };
        Reader<SwitcherooArmedMessage>.read = static reader => new SwitcherooArmedMessage
        {
            WindUpSeconds = reader.Read<float>(),
            UserName = reader.Read<string>(),
        };

        Writer<SwitcherooResultMessage>.write =
            static (writer, message) => writer.Write(message.SwappedCount);
        Reader<SwitcherooResultMessage>.read =
            static reader => new SwitcherooResultMessage { SwappedCount = reader.Read<int>() };

        Writer<SwitcherooUseReplyMessage>.write = static (writer, message) =>
        {
            writer.Write(message.Accepted);
            writer.Write(message.Reason);
            writer.Write(message.Token);
        };
        Reader<SwitcherooUseReplyMessage>.read = static reader => new SwitcherooUseReplyMessage
        {
            Accepted = reader.Read<bool>(),
            Reason = reader.Read<byte>(),
            Token = reader.Read<uint>(),
        };

        Writer<SwitcherooLockMessage>.write =
            static (writer, message) => writer.Write(message.Locked);
        Reader<SwitcherooLockMessage>.read =
            static reader => new SwitcherooLockMessage { Locked = reader.Read<bool>() };

        Plugin.Log.LogInfo("Registered Switcheroo message serializers.");
    }
}
