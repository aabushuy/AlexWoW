# Devlog — M6: Игровые системы

**Статус:** 🟡 в работе. Веха крупная — ведём по срезам (задачи и статусы — в Vikunja,
проект M6 `https://tasks.home.srv/tasks/6`), каждый завершается проверяемым поведением живого клиента 3.3.5a.

Порядок (задан с пользователем): предметы → торговля → бой → спеллы → квесты/лут/ИИ.

| Срез | Статус |
|---|---|
| M6.1 Стартовая экипировка и инвентарь | 🟡 в работе |
| M6.2 Торговля с NPC | ⬜ |
| M6.3 Бой (мили) + серверный тик | ✅ проверено клиентом (бой + плавность движения) |
| M6.4 Спеллы | 🟡 инкр.1 (каст→урон) + инкр.2 (мана+кулдаун) — ✅ клиент; хил — с M6.7; анимация завершения каста — баг M7 |
| M6.5 Квесты | ⬜ |
| M6.6 Лут | ⬜ |
| M6.7 ИИ существ | ⬜ |

---

## M6.4 — спеллы (инкремент 2: мана + кулдаун — ✅ проверено клиентом)

Ресурсная экономика каста: расход маны, реген с «правилом 5 секунд», кулдауны спеллов. Демонстрируется
существующим магом без необходимости в уроне по игроку.

### Что сделано
- **Мана-пул**: `WorldSession.Mana/MaxMana`. `DisplayData.MaxManaForClass` — только мана-классам
  (powertype 0) пул **150** (флэт, точные статы позже); rage/energy/runic → 0 (расход не применяется).
  Инициализация при входе (полный пул); `PlayerSpawn` ставит `UNIT_FIELD_POWER1/MAXPOWER1` из того же
  источника (раньше хардкод 100).
- **Расход**: в `CompleteCast` списываем `ManaCost` спелла, шлём себе ману **двумя пакетами** (см. грабли):
  VALUES-апдейт `UNIT_FIELD_POWER1` + `SMSG_POWER_UPDATE`. Проверка достаточности — **на старте** каста
  (между стартом и завершением мана только растёт), отказ → `SMSG_CAST_FAILED(NO_POWER=0x55)`.
- **Реген**: в серверном тике (`SpellHandlers.TickManaRegenAsync`) +20 маны раз в 1 с, но **только вне
  «правила 5 секунд»** (`LastSpellCastMs`): после каста реген на паузе 5 с → виден чёткий OOM. Апдейт
  полоски только при изменении.
- **Кулдауны**: `WorldSession.SpellCooldowns` (spellId → момент готовности). На завершении спелла с КД —
  `SMSG_SPELL_COOLDOWN 0x134` (запускает полоску на кнопке) + запись; ранний рекаст до готовности →
  `SMSG_CAST_FAILED(NOT_READY=0x43)`. Сброс при выходе из мира.
- **Стоимости/КД (rank 1, как в WotLK)**: Fireball 30 маны, Frostbolt 25, **Fire Blast 40 + КД 8 с**
  (мгновенный — выдан в `SMSG_INITIAL_SPELLS`, теперь демонстрирует кулдаун).

### Грабли / решения
- `SpellCastResult` (3.3.5): `SUCCESS=0x00`, `NOT_READY=0x43`, `NO_POWER=0x55` — сверено с
  `reference world/common.wowm` (значения отличаются по версиям клиента — брал блок `versions = "3.3.5"`).
- `SMSG_CAST_FAILED` (3.3.5): `u8 cast_count + u32 spell + u8 result + Bool multiple_casts`; для
  NOT_READY/NO_POWER conditional-полей нет.
- `SMSG_SPELL_COOLDOWN` (3.3.5): `u64 guid + u8 flags + [{u32 spell, u32 cooldown_ms}]` (массив до конца).
- **🔑 Полоска маны двигается от `SMSG_POWER_UPDATE` (0x480), НЕ от VALUES-апдейта поля.** Сначала слал
  только `UNIT_FIELD_POWER1` (как HP существ в M6.3) — сервер списывал ману (после N кастов «нет маны»),
  но **полоска у клиента не убывала**. Собственному юниту клиент 3.3.5a обновляет бар ресурса из
  отдельного `SMSG_POWER_UPDATE` (`PackedGuid unit + u8 power(MANA=0) + u32 amount`), как TrinityCore на
  каждом изменении power. Шлём оба: поле (консистентность) + `SMSG_POWER_UPDATE` (двигает бар).
- Хил (`SMSG_SPELL_HEAL_LOG`) **отложен**: игрок пока не получает урон (NPC не отвечает до M6.7) — лечить
  нечего, демонстрация будет неубедительной. Вернёмся вместе с ответным боем существ.

### Проверка
- ✅ Сборка чистая (0 предупреждений), тесты 12/12. Новые SMSG-опкоды: `SMSG_SPELL_COOLDOWN 0x134`,
  `SMSG_POWER_UPDATE 0x480` (счётчик «зарегистрированных» опкодов считает только CMSG-хендлеры).
- ✅ **Живой клиент** (маг): спам Fireball/Frostbolt → **полоска маны убывает** → **OOM** («Недостаточно
  маны», каст блокируется) → пауза 5 с → мана регенится до полной; Fire Blast → урон + **кулдаун 8 с** на
  кнопке, ранний рекаст отклоняется.

## M6.4 — спеллы (инкремент 1: каст → урон — ✅ проверено клиентом)

Каст спелла по цели с каст-баром и прямым уроном. Серверный парсер Spell.dbc НЕ нужен (клиент
валидирует механику по своему Spell.dbc) — эффект/школа/время каста хардкожены в `SpellHandlers.Spells`.

### Что сделано
- **Опкоды**: `CMSG_CAST_SPELL 0x12E`, `CMSG_CANCEL_CAST 0x12F`, `SMSG_SPELL_START 0x131`,
  `SMSG_SPELL_GO 0x132`, `SMSG_SPELL_FAILURE 0x133`, `SMSG_SPELLNONMELEEDAMAGELOG 0x250`. Лейауты сверены с reference.
- **`Handlers/SpellHandlers`**: `OnCastSpell` (cast_count u8 + spell u32 + castFlags u8 + SpellCastTargets:
  target_flags u32, UNIT→packed guid). Мгновенный → сразу `CompleteCast`; с временем → `SPELL_START` (каст-бар)
  + **точное завершение `Task.Delay(castMs)`** (поколение `CastGeneration` против отмены/перебивания) → `SPELL_GO`
  (кастеру = снаряд; соседям = `BroadcastToNeighborsAsync`) + урон (`WorldState.ApplyCreatureDamage`, общий с мили)
  + `SMSG_SPELLNONMELEEDAMAGELOG`.
- **Гранты**: Fireball(133)/Frostbolt(116) rank 1 в `SMSG_INITIAL_SPELLS` (req level 1).
- **Прерывание движением**: сдвиг >0.5 ярда → `InterruptOnMoveAsync` + `SMSG_SPELL_FAILURE INTERRUPTED 0x28`.
- **Рефактор M6.3**: общий `WorldState.ApplyCreatureDamage(creature, dmg)`.

### Грабли / уроки
- **`UNIT_MOD_CAST_SPEED` (0x50, float, дефолт 1.0)** — без него клиент читает 0.0 → **анимация каста ломается**
  (масштаб ×0: не стартует). Ставим 1.0 в `PlayerSpawn`. Индекс сверен с reference (калибровка по NPC_FLAGS=0x52).
- **Школа в `SMSG_SPELLNONMELEEDAMAGELOG` — МАСКА (u8), не индекс**: Fire=0x4, Frost=0x10. Слали 2 → «Holy» (маска 0x2).
- **Завершение каста — точно по времени** (Task.Delay): через 250-мс тик GO опаздывал, клиент слал CANCEL_CAST.
- `SPELL_GO` flags=0x100 (как CMaNGOS), `SPELL_START` flags=0 — без conditional-полей.

### 🔴 Открытый баг (вынесен в M7): анимация ЗАВЕРШЕНИЯ каста у кастера не сбрасывается
Поза каста + кнопка не «отжимаются» (снимается прыжком/ESC). Пакеты SPELL_START/GO байт-в-байт верны
(декодированы), тайминг точный, cast-speed/школа/флаги корректны — причина НЕ в спелл-пакетах. Вероятно
не хватает ещё поля юнита (упрощённая модель игрока). Нужен реф-дамп реального каста с CMaNGOS.

### Проверка
✅ Клиент (маг «Магтест»): Fireball/Frostbolt → каст-бар → числа урона (огонь/лёд), HP↓, смерть, респаун;
прерывание движением. 🔴 анимация завершения каста — баг M7.

## M6.3 — бой (мили) + серверный тик (✅ проверено клиентом)

Авто-атака по враждебному NPC: числа урона, падение HP, смерть (труп) и респавн. Вводит
**авторитетные сущности существ** и **серверный тик** — фундамент под спеллы/ИИ/реген.

### Архитектура (вводится здесь)
- **`World/WorldCreature`** — авторитетная сущность существа: мутабельное `Health`/`MaxHealth`,
  `IsAlive`, `RespawnAtMs`; одна на GUID для всех наблюдателей. Прежний per-session immutable
  `NpcSpawn` удалён; `WorldSession.VisibleNpcs` → `ConcurrentDictionary<ulong, WorldCreature>`
  (читает поток тика, пишет поток сессии).
- **Реестр в `WorldState`** (`_creatures`, ConcurrentDictionary): `GetOrAddCreature(guid, factory)` —
  лениво материализует существо из той же DB-строки, что и видимость (`SpawnHandlers`). HP по уровню:
  `WorldCreature.MaxHealthFor(level) = 25 + max(1,level)*12` (упрощённо; точные статы из
  creature_classlevelstats — позже). Без эвикции пока (статические спавны; чистка в M6.8).
- **`World/WorldUpdateLoop : BackgroundService`** — серверный тик `PeriodicTimer` 250 мс →
  `WorldState.UpdateAsync(ct)`: продвигает свинги атакующих игроков, респавнит мёртвых существ.
  Зарегистрирован hosted-сервисом в `Program.cs` перед `WorldListener`.

### Бой (`Handlers/CombatHandlers`)
- `CMSG_SET_SELECTION 0x13D` (plain u64) — хранит `session.SelectionGuid`.
- `CMSG_ATTACKSWING 0x141` (**plain** u64 victim) → `SMSG_ATTACKSTART 0x143` (u64 attacker + u64
  victim); ставит `CombatTargetGuid`, первый свинг — на ближайшем тике.
- `CMSG_ATTACKSTOP 0x142` (пустое) → `SMSG_ATTACKSTOP 0x144` (**packed** player + packed enemy + u32 0).
- Тик `TickMeleeAsync`: раз в `SwingIntervalMs=2000` наносит урон (`8+lvl .. 14+lvl*2`),
  уменьшает `creature.Health`, рассылает наблюдателям `SMSG_ATTACKERSTATEUPDATE 0x14A` + VALUES-апдейт
  `UNIT_FIELD_HEALTH`. На `Health=0` — таймер респавна (`now+30с`) + `SMSG_ATTACKSTOP`.
- `SMSG_ATTACKERSTATEUPDATE`: hit_info=`AFFECTS_VICTIM 0x2` (НЕ `UNK1 0x1` — иначе требуется длинный
  хвост структуры!), attacker(packed)+target(packed)+total_damage+overkill+count(1)+
  [school=1(физ)+dmg_float+dmg_uint]+victim_state=HIT(1)+u32 0+u32 0. Без absorb/resist/block —
  conditional-поля опущены. Лейауты сверены с `reference/wow_messages`.
- Рассылка боя/HP — `WorldState.BroadcastToObserversAsync`/`BroadcastCreatureHealthAsync`
  (наблюдатели = игроки на карте существа, у кого оно в `VisibleNpcs`); атакующий входит в их число.
- HP игрока в спавне — по уровню (`80 + lvl*20`, упрощённо). NPC в M6.3 не отвечает (ответный удар/
  преследование — M6.7).

### Решения / грабли
- `CMSG_ATTACKSWING`/`CMSG_SET_SELECTION`/`SMSG_ATTACKSTART` — **plain** Guid (u64), а `SMSG_ATTACKSTOP`/
  `ATTACKERSTATEUPDATE` — **packed** (сверено по wowm-тестам в reference).
- Существо материализуется в общий реестр при видимости — новый наблюдатель видит правильное текущее HP
  (в т.ч. труп health=0); респавн воскрешает у тех, кто ещё рядом, VALUES-апдейтом.
- Боевое состояние сбрасывается при выходе из мира (`CombatTargetGuid/SelectionGuid = 0`).
- Атаковать можно только враждебного по фракции NPC (клиент сам шлёт ATTACKSWING). Тест-цыплёнок
  (faction 35) дружелюбен — для проверки нужен волк/враг из дампа.

### Часть 1 — ✅ проверена клиентом, закоммичена (`809f75b`)
Числа урона, падение HP, смерть (труп), респавн 30 с, видимость боя вторым клиентом, гейтинг по
мили-радиусу — всё подтверждено. Задеплоено.

### Часть 2 — нормализация времени движения соседей (✅ проверена клиентом)
Чинит «дёрганье» соседних игроков (прыжок вперёд на старте бега, откат на стопе): клиенты имеют
разные точки отсчёта `GetTickCount`, наблюдатель экстраполирует по `(now − packet.time)×speed` →
постоянный сдвиг. **Проверено двумя клиентами: движение соседа стало плавным, прыжок/откат ушли.**
**Ключевой урок:** рерайт — в **серверный** домен одним значением для всех (`time += delta_мувера`,
ровно как TrinityCore `AdjustClientMovementTime`). Перевод в домен наблюдателя
(`+delta_мувера − delta_наблюдателя`) НЕ работает — клиент интерпретирует время движения в серверном.
- **Синхронизация часов:** `SMSG_TIME_SYNC_REQ 0x390` (u32 counter) шлётся каждому игроку при входе и
  далее каждые **10 с из серверного тика** (`WorldState.UpdateAsync` → `WorldEntryHandlers.SendTimeSyncReqAsync`,
  запоминает counter + серверное время отправки). На `CMSG_TIME_SYNC_RESP 0x391` (u32 counter, u32
  client_ticks) считаем `ClockDeltaMs = serverMs − client_ticks` (RTT на LAN пренебрегаем; матчинг по counter).
- **Переписывание времени:** `MovementHandlers` ловит смещение поля `time` в теле (сразу после
  flags2, т.к. длина packed guid переменная) и значение `moverTime`; `WorldState.RelayMovementAsync`
  пишет в тело **серверное время** `moverTime + delta_mover` (через
  `BinaryPrimitives.WriteUInt32LittleEndian` в клон тела) — одинаково всем наблюдателям.
  **Безопасный фолбэк:** пока дельта мувера неизвестна — ретранслируем тело как есть (не хуже
  прежнего поведения; стопгап `NoDelay` остаётся). Диагностика дельт — логом `[timesync]` (Debug).

### Проверка
- ✅ Сборка решения чистая (0 предупреждений), тесты 12/12.
- ⏳ **Живой клиент**: таргет на враждебного NPC → авто-атака → числа урона, HP падает, NPC умирает
  (труп) → через ~30 с респавн. Второй клиент видит бой/смерть/респавн.

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
