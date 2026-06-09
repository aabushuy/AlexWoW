# M9 — Прогрессия (бортовой журнал)

Веха **M9** — прокачка персонажа: опыт/уровни, статы по классу, классовые абилки и тренеры,
профессии. Трекер задач — Vikunja project #12 (доска view 48). Здесь — журнал реализации.

| Срез | Статус |
|---|---|
| **M9.1** XP и уровни (ядро) | ✅ проверено клиентом (`2435ade`) |
| **M9.2** Статы по классу/уровню | ✅ проверено клиентом (`53499b8`) |
| **M9.3** Классовые абилки + тренеры | ✅ проверено клиентом |
| **M9.4** Дев-команды теста прокачки | ✅ проверено клиентом (`581bce9`) |
| **M9.5** Профессии (эпик) | ⬜ |

> M9.1/9.2/9.4 — детали в коммитах и задачах Vikunja. Ключевые узлы (`World/LevelStore`,
> `World/StatStore`, `Handlers/Progression.cs`) — см. также заметку проекта в памяти.
>
> **Продолжение M9.3 (тренеры) в рамках M7** — см. [devlog/M7-tech-debt.md](M7-tech-debt.md):
> #26 тренеры с полным набором умений всех классов в Нортшире (вкл. ДК); #27 гейт рангов по уровню
> (`spell_template.SpellLevel`, т.к. `npc_trainer.reqlevel` в дампе ≈ 0); #28/#29 тренировочный
> манекен + дев-команда `.dummy` (`.dummy` дополняет дев-команды M9.4).

---

## M9.3 — Классовые способности и тренеры

**Проблема:** в M6.4 ВСЕМ классам в `SMSG_INITIAL_SPELLS` слали маг-спеллы
(Fireball/Frostbolt/Fire Blast/Lesser Heal) — заглушка. Нужны корректные классовые наборы +
изучение новых абилок у тренера.

### Что сделано

**1. Стартовые спеллы по классу + персист изученного.**
- `SMSG_INITIAL_SPELLS` (`WorldEntryHandlers.SendInitialSpellsAsync`) теперь собирает книгу из:
  языковые спеллы расы ∪ `playercreateinfo_spell(race,class)` (стартовые абилки класса) ∪
  `character_spell` (выученное у тренера). Маг-спеллы-заглушки M6.4 выдаём **только магу** (класс 8) —
  у их эффектов хардкод под мага; остальным классам они в книге не нужны.
- Таблица `character_spell (owner_guid, spell)` в `alexwow_auth` — только выученное СВЕРХ стартового
  набора (стартовые выдаём по классу при каждом входе, не дублируем в БД).
- Набор кэшируется в `WorldSession.KnownSpells` (для HasSpell-проверок тренера и анти-дубля).
- БД мира: `WorldDatabase.GetStartSpellsAsync(race,class)`.
  `CharactersDatabase.GetLearnedSpellsAsync`/`AddLearnedSpellAsync`.

**2. Тренеры (`Handlers/TrainerHandlers.cs`).**
- `CMSG_TRAINER_LIST` (0x1B0) → `SMSG_TRAINER_LIST` (0x1B1). Ассортимент — `WorldDatabase.GetTrainerAsync(entry)`:
  `npc_trainer` (по entry) ∪ `npc_trainer_template` (по `creature_template.TrainerTemplateId`) +
  тип/класс/раса тренера + `trainer_greeting`. Layout per-spell (38 байт) сверен с CMaNGOS
  `SendTrainerSpellHelper`: `u32 spell, u8 state, u32 cost, u32 talent_cost(0), u32 first_rank(0),
  u8 reqLevel, u32 reqSkill, u32 reqSkillValue, u32 reqAbility[3]`, затем CString greeting.
- `CMSG_TRAINER_BUY_SPELL` (0x1B2) → проверки (тренер подходит классу, состояние GREEN, деньги) →
  списать `spellcost`, `character_spell` + `KnownSpells`, `SMSG_LEARNED_SPELL` (0x12B: u32 spell + u16) +
  `SMSG_TRAINER_BUY_SUCCEEDED` (0x1B3). Отказ → `SMSG_TRAINER_BUY_FAILED` (0x1B4, reason — только консоль).
- **Состояние абилки** (упрощённо, без Spell.dbc): известна → GRAY(2); не хватает уровня или
  незнакома требуемая `ReqAbility` → RED(1); иначе GREEN(0). Skill-требования/цепочки рангов
  (SUPERCEDED) и профессии — отложены.
- **Гейтинг тренера** (`FitsPlayer`): классовый тренер (`TrainerType=0`) — только своему классу
  (`TrainerClass`); расовый гейт, если `TrainerRace` задан.
- **Хук открытия:** `QuestHandlers.OnHello` — если NPC несёт флаг `UNIT_NPC_FLAG_TRAINER` (0x10) и
  подходит игроку, открываем взаимодействие (приоритет над вендором). Флаг берём из
  `VisibleNpcs[guid].Template.NpcFlags`, в БД лезем только для реального тренера.

**Поток открытия окна (грабли, 2 итерации).** Прямой `SMSG_TRAINER_LIST` на правый клик НЕ открывает
окно: у тренеров стоит флаг **GOSSIP (0x1)**, и клиент 3.3.5 для gossip-NPC ждёт **меню госсипа**, а не
сервис-окно. (Вендоры открывались напрямую только потому, что у них gossip-флага нет, напр. Tharynn
Bouden = `128`.) Сверено с CMaNGOS `Player::SendPreparedGossip` — для gossip-NPC он шлёт меню. Поэтому:
1. `OnHello` → `SMSG_GOSSIP_MESSAGE` (0x17D) с одним пунктом «Я хочу обучиться» (иконка TRAINER=3),
   `title_text_id = DEFAULT_GOSSIP_MESSAGE (0xFFFFFF)`.
2. **Вторая грабля:** получив меню, клиент шлёт `CMSG_NPC_TEXT_QUERY` (0x17F) на `title_text_id` и
   **не рисует меню**, пока не получит `SMSG_NPC_TEXT_UPDATE` (0x180). Добавлен обработчик: отвечаем
   8 блоками (формат), заполняем блок 0 (probability=1.0, текст = greeting тренера), остальные нулевые.
3. Игрок выбирает пункт → `CMSG_GOSSIP_SELECT_OPTION` (0x17C) → `TrainerHandlers.OnGossipSelect` →
   `SMSG_TRAINER_LIST` → окно тренера. **Диагностика, которая сработала:** временный INFO-лог на каждый
   `HELLO` + лог «без обработчика» показал ответный опкод `0x17F` — это и указало на недостающий
   npc_text-ответ.

**3. Тест.**
- Дев-команда `.learn <spellId>` (`Handlers/DevCommands.cs`) — выучить абилку без тренера
  (персист + `SMSG_LEARNED_SPELL`). Гарантированный путь проверки книги/каста.
- Реальные классовые тренеры Северолесья (Northshire) подходят сразу — код их «зажигает».
- Опц. `tools/scripts/m9.3-test-trainers.sql` — спавнит реальных классовых тренеров прямо у точки
  старта человека (диапазон guid 9000001..9000099, откат одной строкой). Только тест-сервер.

### Проверка (живой клиент)
Подойти к классовому тренеру → список абилок (цена/уровень/состояние: зелёные доступны, серые
изучены, красные недоступны) → купить доступную → деньги ↓, абилка в книге (P), выносится на панель
и кастуется; недоступные не покупаются; персист через релог (`character_spell`).

### Эталоны
CMaNGOS `NPCHandler.cpp` (`SendTrainerList`/`SendTrainerSpellHelper`/`HandleTrainerBuySpellOpcode`,
`Player::GetTrainerSpellState`), reference `wow_messages` (`smsg_trainer_list`, `cmsg_trainer_buy_spell`,
`smsg_learned_spell`).

### Отложено
Цепочки рангов (`SMSG_SUPERCEDED_SPELL`), тренеры профессий (M9.5), skill-требования,
скидка по репутации, talent-абилки.
