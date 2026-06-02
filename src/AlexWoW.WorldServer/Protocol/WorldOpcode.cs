namespace AlexWoW.WorldServer.Protocol;

/// <summary>Опкоды world-протокола (WotLK 3.3.5a, build 12340). Подмножество для M2–M3.</summary>
public enum WorldOpcode : uint
{
    // Персонажи (M3)
    CmsgCharCreate = 0x036,
    CmsgCharEnum = 0x037,
    CmsgCharDelete = 0x038,
    SmsgCharCreate = 0x03A,
    SmsgCharEnum = 0x03B,
    SmsgCharDelete = 0x03C,
    CmsgPlayerLogin = 0x03D,

    // Вход в мир (M4)
    SmsgLoginSetTimeSpeed = 0x042,
    SmsgTutorialFlags = 0x0FD,
    SmsgUpdateObject = 0x0A9,
    SmsgLoginVerifyWorld = 0x236,

    // Keep-alive
    CmsgPing = 0x1DC,
    SmsgPong = 0x1DD,

    // Handshake (M2)
    SmsgAuthChallenge = 0x1EC,
    CmsgAuthSession = 0x1ED,
    SmsgAuthResponse = 0x1EE,

    // Прочее, что шлёт клиент на экране персонажей
    SmsgAccountDataTimes = 0x209,
    SmsgRealmSplit = 0x38B,
    CmsgRealmSplit = 0x38C,
    CmsgReadyForAccountDataTimes = 0x4FF,
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

/// <summary>
/// Коды ответа на создание/удаление персонажа (ResponseCodes, 3.3.5a).
/// ВНИМАНИЕ: 0x2E = CHAR_CREATE_IN_PROGRESS, success = 0x2F (не off-by-one!).
/// </summary>
public enum CharResponse : byte
{
    CreateInProgress = 0x2E,
    CreateSuccess = 0x2F,
    CreateError = 0x30,
    CreateFailed = 0x31,
    CreateNameInUse = 0x32,
    CreateServerLimit = 0x35,
    CreateAccountLimit = 0x36,

    DeleteInProgress = 0x46,
    DeleteSuccess = 0x47,
    DeleteFailed = 0x48,
}
