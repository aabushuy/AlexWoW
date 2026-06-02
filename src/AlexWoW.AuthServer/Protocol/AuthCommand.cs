namespace AlexWoW.AuthServer.Protocol;

/// <summary>Опкоды логин-протокола (первый байт каждого пакета).</summary>
public enum AuthCommand : byte
{
    LogonChallenge = 0x00,
    LogonProof = 0x01,
    ReconnectChallenge = 0x02,
    ReconnectProof = 0x03,
    RealmList = 0x10,
}
