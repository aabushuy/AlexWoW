# Devlog — M6: Игровые системы

**Статус:** 🟡 в работе. Веха крупная — ведём по срезам (планы в [../TODO/](../TODO/)),
каждый завершается проверяемым поведением живого клиента 3.3.5a.

Порядок (задан с пользователем): предметы → торговля → бой → спеллы → квесты/лут/ИИ.

| Срез | План | Статус |
|---|---|---|
| M6.1 Стартовая экипировка и инвентарь | [TODO/M6.1](../TODO/M6.1-starting-gear.md) | 🟡 в работе |
| M6.2 Торговля с NPC | [TODO/M6.2](../TODO/M6.2-vendor.md) | ⬜ |
| M6.3 Бой (мили) | [TODO/M6.3](../TODO/M6.3-combat.md) | ⬜ |
| M6.4 Спеллы | [TODO/M6.4](../TODO/M6.4-spells.md) | ⬜ |
| M6.5 Квесты | [TODO/M6.5](../TODO/M6.5-quests.md) | ⬜ |
| M6.6 Лут | [TODO/M6.6](../TODO/M6.6-loot.md) | ⬜ |
| M6.7 ИИ существ | [TODO/M6.7](../TODO/M6.7-ai.md) | ⬜ |

---

## M6.1 — стартовая экипировка и инвентарь (🟡 реализовано, ждёт проверки клиентом)

Проблема: персонаж входил в мир голым. Цель — стартовый набор предметов во владении (видимый
шмот на модели, paperdoll, рюкзак), персистентный; фундамент под торговлю (M6.2).

### Что сделано

- **Источник набора — `CharStartOutfit.dbc`.** В дампе CMaNGOS `playercreateinfo_item` **пуст**:
  базовый outfit живёт в клиентском DBC. Добавил офлайн-команду `tools/MapExtractor charstartoutfit
  <dataDir> <out.sql>` (`CharStartOutfit.cs`): WDBC, запись 296 б = id + packed(race|class|gender)
  + itemId[24] + displayId[24] + invType[24]; дедуп по (race,class,itemid) → SQL. Извлёк
  **463 строки / 63 (раса,класс)**, залил в `mangos.playercreateinfo_item` (0 сирот в item_template).
  Клиентские данные в репо не кладём (как maps/vmaps).
- **БД мира** (`WorldDatabase`): `GetStartingItemsAsync(race,class)` (playercreateinfo_item ⨝
  item_template → itemid/amount/InventoryType/stackable), `GetItemTemplateAsync(entry)` (полный
  item_template через динамическую строку Dapper → `ItemTemplateData`), `GetItemDisplaysAsync(entries)`
  (displayid+invType для paperdoll).
- **БД персонажей** (`CharactersDatabase`, БД `alexwow_auth`): таблица **`character_items`**
  (item_guid AUTO_INCREMENT, owner_guid, item_entry, bag=255, slot, stack_count) +
  `HasItemsAsync`/`GetItemsAsync`/`AddItemAsync`; удаление предметов при удалении персонажа.
- **Раскладка набора** (`Handlers/StartingGear.cs`): экипируемое — по слотам 0..18 через
  `InventorySlots.EquipSlotFor(InventoryType)`, прочее — в рюкзак 23..38; slot пишется в БД.
  Выдаётся при создании персонажа и при входе голым (миграция текущих тест-персонажей).
- **Протокол:** опкоды `CMSG_ITEM_QUERY_SINGLE 0x056`/`SMSG_..._RESPONSE 0x058`; индексы
  UpdateFields (сверены с TrinityCore `UpdateFields.h` 3.3.5a): ITEM owner=0x06/contained=0x08/
  stack=0x0E/dur=0x3C/maxdur=0x3D; PLAYER visible_item=0x11B (stride 2), inv_slot_head=0x144
  (контигуально слоты 0..38), coinage=0x492 (задел M6.2).
  - **`ItemObject.BuildCreateObject`** — `TYPEID_ITEM`, movement-блок неживого объекта =
    `UPDATEFLAG_HIGHGUID (0x10)` + `uint32(high)`; GUID предмета = `0x4700<<48 | counter`
    (HIGHGUID_ITEM 12-бит 0x470, как 0xF13 у юнитов). Values: guid/entry/type(0x3)/owner/
    contained/stack/durability.
  - **`PlayerSpawn`** — для себя проставляет guid'ы слотов (`PLAYER_FIELD_INV_SLOT`); для всех —
    видимые предметы (`PLAYER_VISIBLE_ITEM_n_ENTRYID`, slot 0..18) → шмот виден на модели и соседям.
  - **`CharEnum`** — paperdoll: per-слот displayId+invType из экипировки.
  - **`ItemQuery.BuildResponse`** — полный layout `SMSG_ITEM_QUERY_SINGLE_RESPONSE` (stats[N],
    damages[2], spells[5], sockets[3]) по gtker — корректные тултипы.
- **Вход в мир** (`WorldEntryHandlers`): выдать набор, если пусто → загрузить инвентарь в сессию →
  создать item-объекты у клиента (UPDATE_OBJECT) **до** self-спавна → self-спавн с экипировкой.

### Решения / грабли

- `playercreateinfo_item` в дампе пуст — набор пришлось извлекать из DBC (CharStartOutfit).
- High-guid 3.3.5 — **12-битный** (`>>52 & 0xFFF`); `HIGHGUID_ITEM=0x470` (16-бит форма 0x4700).
- Item — не Living: movement-блок = только флаги + high-часть guid (иначе рассинхрон UPDATE_OBJECT).
- Видимость шмота даёт `PLAYER_VISIBLE_ITEM_*`, а не guid слота.
- БД персонажей физически в `alexwow_auth` (общая строка с auth), не отдельная `characters`.
- Durability предметов пока 0 (косметика; точные значения — позже).

### Проверка

- ✅ Сборка чистая (0 предупреждений), тесты 12/12. Экстрактор: 463 строки, 0 сирот.
- ✅ Деплой: world стартовал чисто (39 опкодов), `character_items` создана.
- ⏳ **Живой клиент** (ожидается): новый/существующий персонаж одет на экране выбора и в мире,
  предметы в слотах/рюкзаке, тултипы, персистентность, виден одетым соседу.
