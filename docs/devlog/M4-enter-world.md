# Devlog — M4: Вход в мир

**Статус:** ✅ вход в мир работает — персонаж управляется (камера, бег, прыжки),
проверено живым клиентом. Чат и серверная обработка движения — впереди.

## Главный урок (стоил нескольких итераций)

Одного `SMSG_UPDATE_OBJECT` (спавн) **недостаточно** для управления: персонаж
появляется в мире, но весь ввод заморожен (камера, движение, даже Esc-меню).
Управление разблокирует только **полная последовательность входа**:

`SMSG_LOGIN_VERIFY_WORLD` → `SMSG_ACCOUNT_DATA_TIMES` → `SMSG_FEATURE_SYSTEM_STATUS`
→ `SMSG_TUTORIAL_FLAGS` → `SMSG_LOGIN_SETTIMESPEED` → `SMSG_UPDATE_OBJECT` →
`SMSG_TIME_SYNC_REQ`. Плюс обязательный ответ на `CMSG_UPDATE_ACCOUNT_DATA`
(`SMSG_UPDATE_ACCOUNT_DATA_COMPLETE = 0x463`), иначе клиент зацикливается.

Диагностический приём: лог `CMSG_TIME_SYNC_RESP` подтвердил, что пост-спавн пакеты
доходят (RC4-поток цел) — значит дело было не в шифровании, а в неполном наборе пакетов.

## Цель вехи

По `CMSG_PLAYER_LOGIN` ввести персонажа в мир: клиент уходит с экрана загрузки,
персонаж стоит в стартовой зоне.

## Что сделано (первый шаг M4 — спавн)

- **`CMSG_PLAYER_LOGIN`** → последовательность:
  1. `SMSG_LOGIN_VERIFY_WORLD` (карта + позиция);
  2. `SMSG_TUTORIAL_FLAGS` (8×uint32 = 0);
  3. `SMSG_UPDATE_OBJECT` со спавном собственного игрока.
- **Строительные блоки протокола** (`AlexWoW.WorldServer/Protocol`):
  - `PackedGuid` — упакованный GUID (маска + ненулевые байты);
  - `UpdateField` — индексы полей UpdateFields.h 3.3.5a (object/unit/player);
  - `UpdateMask` — values-блок: «индекс → uint32», сериализация маска+значения;
  - `DisplayData` — модель по расе/полу, фракция по расе, powertype по классу;
  - `PlayerSpawn` — сборка `CREATE_OBJECT2`: movement-блок (флаги Self|Living,
    9 скоростей) + update-mask (guid, type, scale, bytes0, health, level, faction,
    displayId, внешность).

## Детали протокола (3.3.5a)

- `SMSG_UPDATE_OBJECT`: `uint32 blockCount` + блоки. Блок: `uint8 updateType` +
  packed guid + `uint8 typeId` + movement-блок + values-блок.
- `UPDATETYPE_CREATE_OBJECT2 = 3` (для себя), `TYPEID_PLAYER = 4`.
- updateFlags = `SELF(0x01) | LIVING(0x20)`. LIVING-блок: moveFlags, time, xyzo,
  fallTime, 9 скоростей.
- Values: `uint8 blockCount` + маска (uint32×blockCount) + значения по возрастанию индекса.

## Риски / на что смотреть

- **Индексы UpdateFields критичны** — при ошибке клиент крашится или висит на загрузке.
  Values-блок собран из памяти по UpdateFields.h 3.3.5a; финальная проверка — клиент.
- Стартовые предметы и многие поля не заданы (голый персонаж) — для «появиться в мире» ок.

## Проверка

- ✅ Сборка чистая; крипто-тесты без регрессий.
- ✅ **Живой клиент (2026-06-03):** персонаж появляется в стартовой зоне своей расы,
  камера крутится, бег и прыжки работают, Esc-меню открывается, логаут возвращает
  к выбору персонажа.

## Опкоды (уточнены через gtker.com)

- `SMSG_TIME_SYNC_REQ = 0x390`, `SMSG_FEATURE_SYSTEM_STATUS = 0x3C9`,
  `SMSG_UPDATE_ACCOUNT_DATA_COMPLETE = 0x463`,
  `CMSG_LOGOUT_REQUEST = 0x4B` → `SMSG_LOGOUT_RESPONSE = 0x4C` + `SMSG_LOGOUT_COMPLETE = 0x4D`.

## Дальше (в рамках M4 / к M5)

Чат (`CMSG_MESSAGECHAT`), серверная обработка движения (`MSG_MOVE_*` приём+рассылка),
периодический time sync. Затем M5 — видимость и другие сущности.
