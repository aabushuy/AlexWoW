# Devlog — M4: Вход в мир

**Статус:** 🟡 спавн игрока реализован и развёрнут. Ждёт проверки живым клиентом.

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
- ✅ Развёрнуто; world-сервер слушает 8085.
- ⬜ **Живой клиент**: выбрать персонажа → «Вход в мир» → персонаж в стартовой зоне.

## Дальше (в рамках M4)

Движение (приём `MSG_MOVE_*`, рассылка), чат (`CMSG_MESSAGECHAT`), keep-alive,
time sync. Затем M5 — видимость и другие сущности.
