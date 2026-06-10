using System.Text;
using AlexWoW.Common.Network;

namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// Билдер тел SMSG_MESSAGECHAT (3.3.5a): чистые функции «аргументы → байты» (код-стайл §4).
/// Вынесено из Dev.DevChat при конверсии dev-команд в DI (M7 S8); отправкой занимается ChatNotifier.
/// </summary>
internal static class ChatPackets
{
    /// <summary>Системное сообщение игроку (CHAT_MSG_SYSTEM, LANG_UNIVERSAL, отправитель — система).</summary>
    public static byte[] BuildSystemMessage(string text)
    {
        var msg = Encoding.UTF8.GetBytes(text);
        return new ByteWriter(40 + msg.Length)
            .UInt8(0)                       // CHAT_MSG_SYSTEM
            .UInt32(0)                      // LANG_UNIVERSAL
            .UInt64(0)                      // sender (система)
            .UInt32(0)                      // chat flags
            .UInt64(0)                      // target
            .UInt32((uint)(msg.Length + 1)) // SizedCString: длина с нулём
            .Bytes(msg).UInt8(0)
            .UInt8(0)                       // chat tag
            .ToArray();
    }
}
