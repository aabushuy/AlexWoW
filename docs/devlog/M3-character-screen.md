# Devlog — M3: Экран персонажей

**Статус:** 🟡 реализовано и развёрнуто. Ждёт проверки живым клиентом.

## Цель вехи

Снять зависание на «загрузке списка персонажей» и дать рабочий экран:
просмотр, создание и удаление персонажей.

## Что сделано

- **БД `characters`** (`AlexWoW.Database`): таблица + `CharactersDatabase`
  (enum по аккаунту, создание, удаление, проверка имени, лимит 10).
- **`AlexWoW.WorldServer`** — обработчики на экране персонажей:
  - `CMSG_READY_FOR_ACCOUNT_DATA_TIMES` → `SMSG_ACCOUNT_DATA_TIMES` (global mask 0x15);
  - `CMSG_REALM_SPLIT` → `SMSG_REALM_SPLIT` (state 0, date "01/01/01");
  - `CMSG_CHAR_ENUM` → `SMSG_CHAR_ENUM` (полный layout 3.3.5a: guid, внешность,
    level/zone/map/xyz, флаги, пет, 23 слота экипировки нулями);
  - `CMSG_CHAR_CREATE` → валидация (имя занято / лимит) → запись → `SMSG_CHAR_CREATE`;
  - `CMSG_CHAR_DELETE` → удаление по guid+account → `SMSG_CHAR_DELETE`.
- **Стартовые позиции** (`StartPositions`) по расам (mangos playercreateinfo) —
  для корректного фона на экране и спавна в M4.
- `ByteWriter`/`ByteReader` дополнены `UInt64`.

## Детали протокола

- `SMSG_CHAR_ENUM`: `uint8 count` + на каждого персонажа фиксированный блок и цикл по
  `INVENTORY_SLOT_BAG_END` (23) слотам экипировки: `uint32 displayId + uint8 invType + uint32 enchant`.
- Имя: первая буква заглавная, остальные строчные; уникальность — регистронезависимо (collation).
- GUID игрока: high-part = 0, поэтому `uint64(guid) == guid`.
- Коды ответа: `CHAR_CREATE_SUCCESS=0x2E`, `NAME_IN_USE=0x31`, `SERVER_LIMIT=0x34`,
  `CHAR_DELETE_SUCCESS=0x46`, `DELETE_FAILED=0x47`.

## Проверка

- ✅ Сборка чистая, крипто-тесты 12/12 (без регрессий).
- ✅ Развёрнуто; таблица `characters` создаётся при старте world-сервера.
- ⬜ **Живой клиент**: экран «Создать персонажа», создание → персонаж появляется в списке,
  удаление. Стартовые предметы (голый персонаж) — допустимо для M3.

## Дальше → M4

Вход в мир: `CMSG_PLAYER_LOGIN` → `SMSG_LOGIN_VERIFY_WORLD`, стартовые `SMSG_*`,
базовый `SMSG_UPDATE_OBJECT`, движение, чат.
