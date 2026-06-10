using AlexWoW.Common.Network;
using AlexWoW.Database.Models;

namespace AlexWoW.AuthServer.Protocol;

/// <summary>
/// Билдер ответа REALM_LIST: список реалмов → байты пакета. Чистая функция «данные → байты»
/// (политика статиков, code-style §4); состояние соединения остаётся в <c>AuthSession</c>.
/// </summary>
public static class RealmListBuilder
{
    /// <summary>Собирает полный пакет REALM_LIST (опкод + размер + тело).</summary>
    public static byte[] Build(IReadOnlyList<Realm> realms)
    {
        var inner = new ByteWriter(128);
        inner.UInt32(0)                          // unused
             .UInt16((ushort)realms.Count);
        foreach (var realm in realms)
        {
            inner.UInt8(realm.Type)
                 .UInt8(0x00)                    // lock
                 .UInt8(realm.Flags)
                 .CString(realm.Name)
                 .CString($"{realm.Address}:{realm.Port}")
                 .Single(realm.Population)
                 .UInt8(0x00)                    // число персонажей на реалме
                 .UInt8(realm.Timezone)
                 .UInt8((byte)realm.Id);
        }
        inner.UInt8(0x10).UInt8(0x00);           // трейлер

        var innerBytes = inner.ToArray();
        var packet = new ByteWriter(innerBytes.Length + 3)
            .UInt8((byte)AuthCommand.RealmList)
            .UInt16((ushort)innerBytes.Length)
            .Bytes(innerBytes);
        return packet.ToArray();
    }
}
