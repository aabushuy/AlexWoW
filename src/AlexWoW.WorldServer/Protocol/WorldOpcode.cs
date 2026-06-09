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
    CmsgGossipSelectOption = 0x17C,             // выбор пункта меню госсипа (напр. «обучиться»)
    SmsgGossipMessage = 0x17D,                  // меню госсипа (greeting + пункты) — M9.3
    CmsgNpcTextQuery = 0x17F,                   // запрос текста greeting'а по title_text_id — M9.3
    SmsgNpcTextUpdate = 0x180,                  // ответ с текстом greeting'а (8 блоков) — M9.3
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

    // ИИ существ / смерть игрока (M6.7)
    SmsgMonsterMove = 0x0DD,      // packed guid + сплайн движения существа (преследование)
    SmsgAiReaction = 0x13C,       // Guid + u32 reaction (HOSTILE=2 — рык агро)
    CmsgRepopRequest = 0x15A,     // пустое — «отпустить дух» после смерти
    SmsgForcedDeathUpdate = 0x37A, // пустое — сброс таймера release у клиента

    // Лут (M6.6)
    CmsgAutostoreLootItem = 0x108, // u8 loot_slot — забрать предмет из лута
    CmsgLoot = 0x15D,              // u64 guid — открыть лут трупа
    CmsgLootMoney = 0x15E,         // пустое — забрать деньги из открытого лута
    CmsgLootRelease = 0x15F,       // u64 guid — закрыть окно лута
    SmsgLootResponse = 0x160,      // содержимое лута (деньги + предметы)
    SmsgLootReleaseResponse = 0x161, // u64 guid + u8 — подтверждение закрытия
    SmsgLootRemoved = 0x162,       // u8 slot — предмет забран (убрать из окна)
    SmsgLootClearMoney = 0x165,    // пустое — деньги забраны (убрать из окна)

    // Спеллы (M6.4)
    CmsgCastSpell = 0x12E,
    CmsgCancelCast = 0x12F,
    SmsgCastFailed = 0x130,
    SmsgSpellStart = 0x131,
    SmsgSpellGo = 0x132,
    SmsgSpellFailure = 0x133,
    SmsgSpellCooldown = 0x134,
    SmsgSpellHealLog = 0x150,
    SmsgPeriodicAuraLog = 0x24E,                 // тик DoT/HoT (плавающее число) — M10.4b
    SmsgSpellNonMeleeDamageLog = 0x250,
    SmsgPowerUpdate = 0x480,
    SmsgAuraUpdate = 0x496,                      // одна аура на слоте (баффы/дебаффы/формы) — M6.11

    // Тренеры классов (M9.3): список абилок у тренера + покупка
    CmsgTrainerList = 0x1B0,                     // Guid npc — открыть список тренера
    SmsgTrainerList = 0x1B1,                     // guid + trainer_type + спеллы + greeting
    CmsgTrainerBuySpell = 0x1B2,                 // Guid npc + spell — изучить абилку
    SmsgTrainerBuySucceeded = 0x1B3,             // guid + spell — изучено
    SmsgTrainerBuyFailed = 0x1B4,                // guid + spell + reason (только консоль клиента)
    SmsgLearnedSpell = 0x12B,                    // spell + u16 — добавить абилку в книгу
    SmsgSupercededSpell = 0x12C,                 // u32 old + u32 new — высший ранг заменяет низший (M10.3)

    // Квесты (M6.5)
    CmsgQuestQuery = 0x05C,                     // u32 quest_id — данные квеста для журнала
    SmsgQuestQueryResponse = 0x05D,            // полные данные квеста (текст/цели/награды)
    CmsgQuestgiverHello = 0x184,               // Guid npc — открыть квесты NPC
    SmsgQuestgiverQuestList = 0x185,           // список квестов NPC
    CmsgQuestgiverQueryQuest = 0x186,          // Guid + quest_id — запрос деталей
    SmsgQuestgiverQuestDetails = 0x188,        // окно деталей квеста (accept)
    CmsgQuestgiverAcceptQuest = 0x189,         // Guid + quest_id — принять квест
    CmsgQuestgiverCompleteQuest = 0x18A,       // Guid + quest_id — сдать (открыть награду)
    CmsgQuestgiverRequestReward = 0x18C,       // Guid + quest_id — запрос окна награды
    SmsgQuestgiverOfferReward = 0x18D,         // окно сдачи квеста (награды)
    CmsgQuestgiverChooseReward = 0x18E,        // Guid + quest_id + reward — выбор награды
    SmsgQuestgiverQuestComplete = 0x191,       // квест завершён (награда выдана)
    SmsgQuestupdateComplete = 0x198,           // quest_id — цель выполнена
    SmsgQuestupdateAddKill = 0x199,            // прогресс убийства цели
    CmsgQuestgiverStatusQuery = 0x182,         // Guid npc — статус одного квестгивера
    SmsgQuestgiverStatus = 0x183,              // Guid + u32 status (иконка !/?)
    CmsgQuestgiverStatusMultipleQuery = 0x417, // пустое — статусы всех видимых
    SmsgQuestgiverStatusMultiple = 0x418,      // u32 count + [u64 guid + u8 status]

    // Прогрессия (M9.1)
    SmsgLevelupInfo = 0x1D4,       // new_level + health + 7 powers + 5 stats (диффы) — сплеш «ding»

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
    CmsgSetActionButton = 0x128,                // игрок повесил/снял ярлык на панель (M7 #17)
    SmsgActionButtons = 0x129,                  // выдача ярлыков панелей при входе (M7 #17)
    CmsgSetActionbarToggles = 0x2BF,            // показ доп. панелей (PLAYER_FIELD_BYTES[2]) (M7 #17)
    SmsgInitializeFactions = 0x122, // список репутаций при входе (M7 #11) — инициализирует rep-менеджер клиента
    SmsgTutorialFlags = 0x0FD,
    SmsgUpdateObject = 0x0A9,
    SmsgDestroyObject = 0x0AA,
    SmsgLoginVerifyWorld = 0x236,
    SmsgTimeSyncReq = 0x390,
    CmsgTimeSyncResp = 0x391,

    // Календарь (минимум): отвечаем на запрос числа ожидающих приглашений (0), чтобы индикатор
    // на часах миникарты не висел и опкод не сыпал «без обработчика». (Taint TimeManager лечит SMSG_ADDON_INFO.)
    CmsgCalendarGetNumPending = 0x447,
    SmsgCalendarSendNumPending = 0x448,

    // Keep-alive
    CmsgPing = 0x1DC,
    SmsgPong = 0x1DD,

    // Handshake (M2)
    SmsgAuthChallenge = 0x1EC,
    CmsgAuthSession = 0x1ED,
    SmsgAuthResponse = 0x1EE,
    // Ответ на список аддонов из CMSG_AUTH_SESSION. Без него клиент считает Blizzard-аддоны
    // (TimeManager/Calendar) не доверенными → taint → блокировка защищённого действия по Esc.
    SmsgAddonInfo = 0x2EF,

    // Прочее, что шлёт клиент на экране персонажей / при входе
    SmsgFeatureSystemStatus = 0x3C9,
    SmsgAccountDataTimes = 0x209,
    CmsgRequestAccountData = 0x20A,              // клиент запрашивает блоб account-data (M7 #17)
    CmsgUpdateAccountData = 0x20B,
    SmsgUpdateAccountData = 0x20C,               // ответ с сохранённым блобом (M7 #17)
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
