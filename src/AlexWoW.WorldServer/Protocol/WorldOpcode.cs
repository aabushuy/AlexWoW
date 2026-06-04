namespace AlexWoW.WorldServer.Protocol;

/// <summary>Опкоды world-протокола (WotLK 3.3.5a, build 12340). Подмножество для M2–M3.</summary>
public enum WorldOpcode : uint
{
    // Чат (M4)
    CmsgMessageChat = 0x095,
    SmsgMessageChat = 0x096,

    // Движение (M4) — все несут packed guid + MovementInfo
    MsgMoveStartForward = 0x0B5,
    MsgMoveStartBackward = 0x0B6,
    MsgMoveStop = 0x0B7,
    MsgMoveStartStrafeLeft = 0x0B8,
    MsgMoveStartStrafeRight = 0x0B9,
    MsgMoveStopStrafe = 0x0BA,
    MsgMoveJump = 0x0BB,
    MsgMoveStartTurnLeft = 0x0BC,
    MsgMoveStartTurnRight = 0x0BD,
    MsgMoveStopTurn = 0x0BE,
    MsgMoveStartPitchUp = 0x0BF,
    MsgMoveStartPitchDown = 0x0C0,
    MsgMoveStopPitch = 0x0C1,
    MsgMoveSetRunMode = 0x0C2,
    MsgMoveSetWalkMode = 0x0C3,
    MsgMoveFallLand = 0x0C9,
    MsgMoveStartSwim = 0x0CA,
    MsgMoveStopSwim = 0x0CB,
    MsgMoveSetFacing = 0x0DA,
    MsgMoveSetPitch = 0x0DB,
    MsgMoveHeartbeat = 0x0EE,

    // Логаут (M4)
    CmsgLogoutRequest = 0x04B,
    SmsgLogoutResponse = 0x04C,
    SmsgLogoutComplete = 0x04D,
    CmsgLogoutCancel = 0x04E,
    SmsgLogoutCancelAck = 0x04F,

    // Запросы (M4)
    CmsgNameQuery = 0x050,
    SmsgNameQueryResponse = 0x051,
    CmsgQueryTime = 0x1CE,
    SmsgQueryTimeResponse = 0x1CF,

    // Предметы (M6.1)
    CmsgItemQuerySingle = 0x056,
    SmsgItemQuerySingleResponse = 0x058,

    // Управление инвентарём (M6.9)
    CmsgAutoEquipItem = 0x10A,
    CmsgAutostoreBagItem = 0x10B,
    CmsgSwapItem = 0x10C,
    CmsgSwapInvItem = 0x10D,
    CmsgSplitItem = 0x10E,
    CmsgAutoEquipItemSlot = 0x10F,
    CmsgDestroyItem = 0x111,
    SmsgInventoryChangeFailure = 0x112,

    // Торговля с NPC (M6.2)
    CmsgGossipHello = 0x17B,
    CmsgSellItem = 0x1A0,
    SmsgSellItem = 0x1A1,
    CmsgBuyItem = 0x1A2,
    SmsgBuyItem = 0x1A4,
    SmsgBuyFailed = 0x1A5,
    CmsgListInventory = 0x19E,
    SmsgListInventory = 0x19F,

    // Бой (M6.3)
    CmsgSetSelection = 0x13D,
    CmsgAttackSwing = 0x141,      // Guid victim (plain u64)
    CmsgAttackStop = 0x142,       // пустое тело
    SmsgAttackStart = 0x143,      // u64 attacker + u64 victim
    SmsgAttackStop = 0x144,       // packed player + packed enemy + u32 0
    SmsgAttackSwingNotInRange = 0x145, // пустое — цель вне мили-радиуса
    SmsgAttackerStateUpdate = 0x14A,

    // Видимость / NPC (M5)
    CmsgCreatureQuery = 0x060,
    SmsgCreatureQueryResponse = 0x061,
    CmsgGameObjectQuery = 0x05E,
    SmsgGameObjectQueryResponse = 0x05F,

    // Персонажи (M3)
    CmsgCharCreate = 0x036,
    CmsgCharEnum = 0x037,
    CmsgCharDelete = 0x038,
    SmsgCharCreate = 0x03A,
    SmsgCharEnum = 0x03B,
    SmsgCharDelete = 0x03C,
    CmsgPlayerLogin = 0x03D,

    // Склонения имени (ruRU-клиент, после создания персонажа)
    CmsgSetPlayerDeclinedNames = 0x419,
    SmsgSetPlayerDeclinedNamesResult = 0x41A,

    // Вход в мир (M4)
    SmsgLoginSetTimeSpeed = 0x042,
    SmsgInitialSpells = 0x12A,
    SmsgTutorialFlags = 0x0FD,
    SmsgUpdateObject = 0x0A9,
    SmsgDestroyObject = 0x0AA,
    SmsgLoginVerifyWorld = 0x236,
    SmsgTimeSyncReq = 0x390,
    CmsgTimeSyncResp = 0x391,

    // Keep-alive
    CmsgPing = 0x1DC,
    SmsgPong = 0x1DD,

    // Handshake (M2)
    SmsgAuthChallenge = 0x1EC,
    CmsgAuthSession = 0x1ED,
    SmsgAuthResponse = 0x1EE,

    // Прочее, что шлёт клиент на экране персонажей / при входе
    SmsgFeatureSystemStatus = 0x3C9,
    SmsgAccountDataTimes = 0x209,
    CmsgUpdateAccountData = 0x20B,
    SmsgUpdateAccountDataComplete = 0x463,
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
