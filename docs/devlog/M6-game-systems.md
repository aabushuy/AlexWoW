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

## M6.9 — управление инвентарём (🟡 реализовано, ждёт проверки клиентом)

Манипуляции с предметами в сумке (M6.1 давал только отображение).

### Что сделано (`InventoryHandlers`, опкоды сверены с локальной wow_messages)
- `CMSG_SWAP_INV_ITEM 0x10D` (src/dst slot) и `CMSG_SWAP_ITEM 0x10C` — перемещение/обмен слотов
  основного контейнера (bag 255); `CMSG_AUTOSTORE_BAG_ITEM 0x10B` — в первый свободный слот рюкзака.
- `CMSG_SPLIT_ITEM 0x10E` (src/dst bag/slot + u32 amount) — **сплит стопки** в пустой слот рюкзака:
  уменьшаем стек источника (`ITEM_FIELD_STACK_COUNT` VALUES-апдейт), создаём новый item на `amount`.
- `CMSG_AUTOEQUIP_ITEM 0x10A` / `CMSG_AUTOEQUIP_ITEM_SLOT 0x10F` — экипировка кликом: слот по
  `InventoryType` (`InventorySlots.EquipSlotFor`/`CanEquipInSlot`, альт-слоты для колец/тринкетов).
- `CMSG_DESTROYITEM 0x111` — выброс (вся стопка → DESTROY object; часть → уменьшение стека).
- Слоты-контейнеры/видимая экипировка обновляются одним player-VALUES-апдейтом
  (`PlayerSpawn.BuildPlayerValuesUpdate`: `PLAYER_FIELD_INV_SLOT` + `PLAYER_VISIBLE_ITEM_*`).
- БД: `MoveItemAsync`/`SetItemStackAsync` (персист slot/stack); `InventoryItem` поля сделаны изменяемыми.

### Решения / заметки
- Всё в основном контейнере `bag=255` (доп. сумки-контейнеры не поддержаны — ignore).
- Сервер авторитетен: при отказе пере-утверждаем состояние слотов (предмет «возвращается»);
  отдельный `SMSG_INVENTORY_CHANGE_FAILURE` пока не шлём (опкод заведён).
- Валидация экипировки по `InventoryType`; своп при занятом целевом слоте.
- Сплит — только в **пустой** слот рюкзака (мерж в занятый — позже).

### Проверка
- ✅ Сборка чистая, тесты 12/12. World: 51 опкод.
- ⏳ **Живой клиент**: сплит стопки (5→2+3), перетаскивание/обмен в сумке, экипировка кликом и
  снятие, выброс; релог — раскладка сохранена.

## M6.2 — торговля с NPC / вендор (🟡 реализовано, ждёт проверки клиентом)

Открытие окна вендора, покупка и продажа, деньги.

### Что сделано
- **Деньги:** колонка `characters.money` (медь), миграция через `ALTER ... ADD COLUMN` (глушим
  ошибку 1060 «дубликат»), стартовый баланс **100g** (тест). Поле `PLAYER_FIELD_COINAGE (0x492)`
  в спавне (private, только себе) + VALUES-апдейт `PlayerSpawn.BuildCoinageUpdate` после сделок.
- **Ассортимент:** `WorldDatabase.GetVendorItemsAsync(entry)` — `npc_vendor ⨝ item_template`,
  только за золото (`ExtendedCost=0`, без условий). entry существа берём из его GUID (`>>24 & 0xFFFFFF`).
- **Опкоды** (сверены с локальной wow_messages): `CMSG_GOSSIP_HELLO 0x17B` / `CMSG_LIST_INVENTORY 0x19E`
  → `SMSG_LIST_INVENTORY 0x19F` (vendor, u8 count, по предмету 8×u32: muid, entry, displayId,
  maxItems[0xFFFFFFFF=∞], price, maxDurability, buyCount, extendedCost). `CMSG_BUY_ITEM 0x1A2` →
  `SMSG_BUY_ITEM 0x1A4` / `SMSG_BUY_FAILED 0x1A5`. `CMSG_SELL_ITEM 0x1A0` → `SMSG_SELL_ITEM 0x1A1` (на ошибке).
- **Открытие окна:** на gossip-hello/list для вендора сразу шлём `SMSG_LIST_INVENTORY` (без gossip-меню).
- **Покупка** (`VendorHandlers`): проверка денег и места в рюкзаке → списываем деньги, кладём предмет
  (`character_items` + сессия), создаём item-объект у клиента + привязка к слоту (`BuildInvSlotUpdate`) +
  обновление денег + `SMSG_BUY_ITEM`. Ошибки → `SMSG_BUY_FAILED` (нет денег/места/предмета).
- **Продажа:** ищем предмет по GUID в инвентаре, удаляем (`character_items`+сессия), начисляем
  `item_template.SellPrice`, шлём DESTROY предмета + очистку слота + обновление денег.
- Деньги персистятся сразу при сделке (`SetMoneyAsync`).

### Грабли / заметки
- `npc_vendor`: 34k строк; `maxcount=0` = бесконечно (шлём 0xFFFFFFFF). `ExtendedCost>0` (хонор/жетоны) пока пропускаем.
- Стартовые 100g — тестовое значение; экономику/стартовый баланс настроим позже.
- Продажа пока удаляет всю строку-стопку (стартовые предметы не стопкуются); частичная продажа стопки — позже.
- Покупка не мёржит стопки — каждая покупка в отдельный слот рюкзака.

### Проверка
- ✅ Сборка чистая, тесты 12/12. Деплой: world 44 опкода, миграция money ок (все по 100g).
- ⏳ **Живой клиент**: правый клик по вендору → окно товаров; купить (деньги ↓, предмет в сумке);
  продать (предмет ↓, деньги ↑); тултип цены; релог — деньги/предметы сохранены.

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
