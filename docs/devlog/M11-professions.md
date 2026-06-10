# M11 — Профессии

Базовая система профессий: изучить профессию у тренера → собирать ресурсы (mining/herbalism) и/или
крафтить из реагентов → качать навык. Выделена в отдельную веху из эпика M9.5. Сделана поверх готовой
инфраструктуры (тренеры M9.3, обучение спеллам M10, лут, выдача предметов, dev-спавн, пайплайн каста).
Задачи — **Vikunja** (project M11, `https://tasks.home.srv`).

| Срез | Статус |
|---|---|
| **M11.1** Скилл-инфраструктура (поля скиллов + персист) | ✅ |
| **M11.2** Изучение профессии у тренера | ✅ |
| **M11.3** Крафт (CREATE_ITEM + реагенты + skill-up) | ✅ |
| **M11.4** Сбор ресурсов (ноды mining/herbalism, CMSG_GAMEOBJ_USE) | ✅ |
| **M11.5** Прокачка скилла (формула цветов + тиры/кап) | ✅ |
| **M11.6** (стретч) Данные из DBC / расширение каталога | ⬜ |

---

## Решения по данным

Дамп `mangos` (Spell.dbc/Item.dbc как таблицы) содержит **всё для крафта**: `spell_template` несёт
`EffectItemType*` (создаваемый предмет), `Reagent1-8`/`ReagentCount1-8` (реагенты), `EffectMiscValue*`
(id навыка для спеллов-профессий). Поэтому крафт и привязка «спелл→навык» — **из реальных данных**.

Чего в SQL нет (только в клиентских DBC, ридера нет): `skill_line_ability` (цветовые пороги рецептов) и
`Lock.dbc` (требуемый скилл ноды). Их заменяет **курированный сид** под стартовые профессии
(`Professions.Recipes` — рецепт→навык/req; `Professions.Nodes` — нода→навык/req/ресурс). Полный каталог
из DBC — опциональный M11.6.

## Как кодируются профессии в spell_template (выяснено по дампу)

- Спелл, **изучающий профессию**, несёт эффект `SKILL`(118) или `SKILL_STEP`(44) с `EffectMiscValue` =
  id навыка (напр. Blacksmithing 2018 → 164; Apprentice Skinner 8615 → 393, эффект 44).
- **Тир (потолок)** кодируется в `EffectBasePoints` этого эффекта: BP=0 → апрентис (75), BP=2 →
  подмастерье (150), BP=3 → эксперт (225) … BP=6 → грандмастер (450). Формула:
  `max = clamp(max(1, BP) × 75, 75, 450)`. Изучение спелла след. тира поднимает кап (`Skills.SetMax`).
- **Рецепт крафта** — эффект `CREATE_ITEM`(24): `EffectItemType` = результат, `count = BasePoints+1`
  (Smelt Bronze BP=1 → 2 слитка), `Reagent*`/`ReagentCount*` — расход.

## Реализация по слоям

- **Скиллы (M11.1):** таблица `character_skill` (миграция `AddCharacterSkill`) + методы в
  `ICharacterStateRepository`. На сессии — `PlayerSkillBook` (`World/PlayerSkills.cs`): слоты
  `PLAYER_SKILL_INFO` = языковые (по расе) + профессии (персист). Запись в спавн — обобщён цикл языков в
  `PlayerSpawn.BuildValues`; одиночный апдатый слота при изменении — `Handlers/Skills.cs`.
- **Изучение (M11.2/M11.5):** хук в `SpellLearn.GrantAsync` — если изучаемый спелл выдаёт навык
  (`Professions.SkillGrantedBy`), выдаём линию с потолком тира.
- **Крафт (M11.3):** `SpellCatalog` парсит эффект 24 + реагенты в `SpellInfo`; `Handlers/Crafting.cs`
  на завершении каста: проверка реагентов (отказ `SMSG_CAST_FAILED` REAGENTS=0x64 ещё на старте) →
  расход (`InventoryGrant.ConsumeAsync`, вынесен из QuestHandlers) → создание (`TryGiveAsync`) → skill-up.
- **Сбор (M11.4):** новый опкод `CMSG_GAMEOBJ_USE`(0x0B1) → `Handlers/GameObjectUseHandlers.cs`: entry
  ноды из GUID GO, гейт по навыку, выдача ресурса, skill-up, истощение (деспавн dev-GO/DESTROY).
- **Прокачка (M11.5):** `Professions.SkillUpChance(value, req)` — полосы оранж/жёлт/зелён/серый (band 25),
  по эталону CMaNGOS `SkillGainChance`; кап — в `Skills.AddValueAsync`.

## Эталоны
CMaNGOS `Player::UpdateCraftSkill`/`UpdateGatherSkill`/`SkillGainChance`/`SetSkill`,
`Spell::EffectCreateItem`/`TakeReagents`, `GameObject::GetRequiredLockSpell`.

## Дев-команды для проверки
- `.proftrainer <prof>` — тренер профессии (учим профессию у него или `.learn <recipeSpellId>`).
- `.skill <skillId> <value> [max]` — выставить навык напрямую (тест M11.1).
- `.node copper|tin|silver|iron|peacebloom|silverleaf|earthroot|mageroyal|<entry>|off` — нода сбора.
- `.craft forge|anvil|cookfire|mailbox|off` — крафт-станок (для рецептов, требующих станок).

**Сквозной тест (Mining + Blacksmithing):**
1. `.proftrainer mining` → выучить Mining у тренера (навык 186 = 1/75).
2. `.node copper` → кликнуть жилу → Copper Ore + skill-up.
3. `.learn 2657` (Smelt Copper) → каст → Copper Ore → Copper Bar, Mining растёт.
4. `.proftrainer blacksmithing` → выучить; `.craft forge`; `.learn 2660`; накрафтить → Blacksmithing растёт.

## Заметки / развитие
- Сбор даёт ресурс напрямую (без окна лута) — упрощение; окно лута для нод — возможная полировка.
- Skill-up привязан к навыку только для рецептов/нод из сида; полный каталог рецептов — M11.6 (DBC).
- Лимит «2 основные профессии», специализации, открытия алхимии, рыбалка — позже.
