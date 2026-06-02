namespace AlexWoW.WorldServer.Protocol;

/// <summary>Опкоды world-протокола (WotLK 3.3.5a, build 12340). Подмножество для M2+.</summary>
public enum WorldOpcode : uint
{
    // Handshake
    SmsgAuthChallenge = 0x1EC,
    CmsgAuthSession = 0x1ED,
    SmsgAuthResponse = 0x1EE,

    // Keep-alive
    CmsgPing = 0x1DC,
    SmsgPong = 0x1DD,

    // Персонажи (M3)
    CmsgCharEnum = 0x037,
    SmsgCharEnum = 0x03B,
    CmsgPlayerLogin = 0x03D,
}

/// <summary>Код ответа SMSG_AUTH_RESPONSE.</summary>
public enum AuthResponseCode : byte
{
    Ok = 0x0C,             // AUTH_OK
    Failed = 0x0D,
    Unavailable = 0x0E,
    SystemError = 0x0F,
    WaitQueue = 0x1B,
}
