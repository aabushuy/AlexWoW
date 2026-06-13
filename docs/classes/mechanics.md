# Механики — кросс-индекс для проверки (WoW 3.3.5a)

Главная ось работы. Движок спеллов **data-driven из `spell_template`**, поэтому правка ОДНОЙ механики зажигает
абилку сразу у всех классов/школ. Чиним и проверяем **по механике**, а не по школе/классу.
Статусы: ✅ проверено · 🟡 реализовано · ⬜ не сделано · ➖ вне этапа. Детали по абилке — в `<class>-abilities.md`.

## Фаза 1 — массовая сверка чисел (харнес M12)

Прямой урон/хил/DoT/HoT уже data-driven (отметки 🟡 в файлах классов). Проверяются **автоматически**:
в игре админом — `.dummy` (+`.dummy heal`), затем `.spelltest run` → сверка вычисленного с `spell_template`,
аномалии на `/Admin` (Spell QA). Прогнать по классам, перевести 🟡→✅ или вскрыть пробел в `SpellCatalog.FromTemplate`.

**Прогресс:** Воин — сессия #3 (чистые таланты, ур.80): 0 аномалий, 18 связок спелл+тип в эталоне.
Сверены Rend (DoT), Thunder Clap, Victory Rush, Heroic/Shattering Throw → ✅. Заодно закрыт пробел
анализатора: для weapon-абилок теперь проверяется «ниже минимума» (непрокинутый flat-бонус) — раньше глушилось.
Паладин — сессия #2 (чистые таланты): 0 аномалий. Сверены Holy Light/Flash of Light (хил), Exorcism,
Holy Wrath, Avenger's Shield, Shield of Righteousness, Hammer of Wrath (урон) → ✅.
Маг — сессия #4 (чистые таланты): 0 аномалий, 21 связка. Школы (Fire/Frost/Arcane/Frostfire) парсятся верно.
Сверены Fireball/Pyroblast/Frostfire Bolt/Living Bomb (+DoT), Fire Blast, Scorch, Flamestrike, Blast Wave,
Dragon's Breath, Frostbolt, Ice Lance, Frost Nova, Cone of Cold, Arcane Explosion/Barrage → ✅.
Не покрыто харнесом: Arcane Blast, Arcane Missiles (канал), Blizzard (канал) — остаются 🟡.
**Фаза 1 итог по Воин/Маг/Паладин: чисел-аномалий нет — движок SpellCatalog.FromTemplate считает урон/хил/DoT корректно.**

## Фаза 2 — механики по убыванию отдачи

### 1. Формы / toggle (эксклюзивные переключатели) — каркас ЕСТЬ (`AuraService`, группы)
| Что | Класс | Статус |
|---|---|---|
| Стойки (Battle/Def/Berserker) | Воин | ✅ |
| Ауры паладина (Devotion/Retri/Concentration) | Паладин | ✅ |
| Аспекты охотника (Hawk/Cheetah/Monkey) | Охотник | ✅ |
| Брони мага (Frost/Mage/Molten Armor) | Маг | ✅ (эксклюзив проверен вживую; стат не моделируется) |
| Печати (Seal of Righteousness/Light/Wisdom/Justice) | Паладин | ✅ (эксклюзив + on-hit: holy/хил/мана/стан проверены; величины упрощённые) |
| Облик Тьмы (Shadowform) | Жрец | ✅ (форма 28 + **+15% Shadow** прямой+DoT; выход кнопкой в 1 клик + повторный вход без релога — `SMSG_COOLDOWN_EVENT`, см. ниже; Avenging Wrath +20% тоже ✅) |
| Формы (Bear/Cat/Travel/Moonkin/Tree) | Друид | 🟡 (каркас форм) |
| Присутствия (Blood/Frost/Unholy) | DK | 🟡 (эксклюзивная группа, форма 0) |
| Брони (Demon Skin/Demon Armor/Fel Armor) | Чернокнижник | ✅ (тот же механизм, проверен на маге; стат не моделируется) |
| Метаморфоза | Чернокнижник | ⬜ |
| Призрачный волк (Ghost Wolf) | Шаман | 🟡 (toggle-форма 16) |
| Скрытность (Stealth) | Разбойник | ✅ (toggle-форма 30, все ранги; выход + повторный вход без релога — `SMSG_COOLDOWN_EVENT`, кулдаун 10с) |

> **Выход из формы и `SPELL_ATTR_COOLDOWN_ON_EVENT` (бит 25).** У Shadowform/Stealth кулдаун
> (Category CD: Shadowform 1.5с, Stealth 10с) стартует **не на касте, а при СНЯТИИ ауры**. Клиент
> держит кнопку «активной», пока не получит `SMSG_COOLDOWN_EVENT` (0x135: `u32 spell + guid`),
> переводящий спелл `active → cooldown → ready`. Без него кнопка залипала «дожатой»/недоступной
> («Заклинание пока недоступно») до релога. `AuraService` шлёт событие при реальном снятии аура-формы
> (`resetForm`) со спелла с этим атрибутом (`SpellInfo.CooldownOnAuraRemove`). Эталон — CMaNGOS
> `Player::AddCooldown` на fade COOLDOWN_ON_EVENT-ауры. Ghost Wolf атрибута не имеет — не затронут.

### 2. Ресурсы — ярость/энергия/мана + combo points ✅; руны/осколки НЕТ
| Ресурс | Класс | Статус | Зажигает |
|---|---|---|---|
| Combo points | Разбойник, друид-кошка | ✅ (CP.1–CP.3b) | генераторы (Sinister Strike/Backstab/Rake) копят; финишеры гейтятся/расходуют, урон+тик и длительность скалируются очками (Eviscerate/Rupture/Slice and Dice/Kidney Shot) |
| Руны + runic power | DK | ✅ (RUNE.1–5) | почти все абилки DK |
| Осколки/камни души | Чернокнижник | ⬜ | призывы/Soulstone/Healthstone-гейт |

> **Руны DK (RUNE.1–5 — ✅ проверено вживую: гашение/таймер рун, трата+сила рун при касте, death-руны).** 6 рунных
> слотов (`SessionCombatState.Runes`, раскладка Blood,Blood,Unholy,
> Unholy,Frost,Frost — эталон mangos `runeSlotTypes`), каждый слот = `{BaseType, CurrentType, CooldownMs}`.
> Инициализация при входе в мир только у DK (`RuneService.Initialize`); поля рун (`POWER_RUNE`=5 max 8 + готовые,
> `PLAYER_RUNE_REGEN_1..4`) кладутся в спавн, полный снимок — `SMSG_RESYNC_RUNES` (0x487) после спавна. Дев-команда
> `.runes [ready|spend <тип>]` — проверка. **RUNE.2:** реген рун по кулдауну (10с, параллельно — эталон mangos
> `Regenerate(POWER_RUNE)`) в `WorldTick`; готовая руна шлёт снимок. **RUNE.3:** стоимость рун по абилке
> (`RuneService.RuneCosts` — таблица по **SpellFamilyFlags**, т.к. они одинаковы у всех рангов; ключ по spellId/RuneCostID
> не годится — меняется по рангу, на ур.80 высокий ранг не находился; SpellRuneCost.dbc нет в БД); гейт каста (нет рун → NO_POWER),
> расход (ставит руны на КД) + начисление силы рун; death-руна — джокер под любой тип. **RUNE.4:** сила рун
> (runic power, POWER_RUNIC_POWER=6, ×10) — тратится RP-абилками (Frost Strike/Death Coil, PowerType=6 ManaCost=400=40RP
> через общий ресурс-гейт), распад вне боя (`CombatResourcesService`), дев-команда `.rp [0-100]`. **RUNE.5:** death-руны —
> конвертация слота (`SMSG_CONVERT_RUNE`), death — джокер под любой тип в стоимости; Blood Tap (45529) конвертит руну
> крови в death и активирует; дев-проверка `.runes death [slot]`. **Todo:** ускорение регена (Unholy Presence /
> рейтинг скорости) · точные значения SpellRuneCost · авто-конверт (Blood of the North/Reaping) · реверт death→base.

### 3. Митигейшн / avoidance / absorb — РАЗОГНАЛИСЬ (блок/Глухая оборона)
| Аура | Абилки (класс) | Статус |
|---|---|---|
| MOD_BLOCK_PERCENT (51) | Блок щитом (Воин) ✅, Holy Shield (Пал) ⬜ | частично |
| MOD_DAMAGE_PERCENT_TAKEN (87) | Глухая оборона (Воин) ✅; Divine Protection (Пал), Pain Suppression/Dispersion (Жрец), Barkskin (Друид), Icebound Fortitude (DK), Shamanistic Rage (Шам) | каркас ✅, остальные ⬜ |
| MOD_DODGE_PERCENT (49) | Evasion (Разб), Quickness-расовый | ⬜ |
| SCHOOL_ABSORB / MANA_SHIELD | PW:Shield (Жрец) ✅, Ice Barrier/Fire/Frost Ward (Маг) ✅, Mana Shield (Маг) ✅; Anti-Magic Shell (DK), Sacred Shield (Пал) ⬜ | ✅ ABS.1–ABS.2 (поглощение мили существ по школе + absorb в damage-логе; Mana Shield тратит ману. Todo: персист через релог, поглощение спелл-урона существ) |
| SCHOOL_IMMUNITY / DAMAGE_IMMUNITY (39/40) | Divine Shield (Пал) ✅, Ice Block (Маг) ✅, Hand of Protection (Пал) ✅ | 🟡 IMMUNITY.1 (data-driven по аурам 39/40: маска школ из всех таких эффектов; пока активна аура — входящий мили-урон совпадающей школы гасится в ноль, клиент рисует «Иммунитет» VictimState=7. Стенд: каст «пузыря» → `.dummy attack`. Todo: иммунитет к спелл-урону существ (каст-манекен бьёт вхолостую), снятие свинга существа, untargetable/Divine Intervention, персист) |

### 4. CC (контроль) — фреймворк ЕСТЬ (`CrowdControlService`, data-driven по CC-ауре)
Детект CC в `SpellCatalog.FromTemplate` (ауры 12/26/7/27/5 → Stun/Root/Fear/Silence/Disorient + длительность).
На существе-цели: визуал (аура-дебафф + UNIT_FLAG) + состояние; стан/страх/дезориентация **блокируют свинг**
существа (`CreatureCombatAI`); истечение — в `WorldTick` (у любого существа, в т.ч. манекена). PvE-фокус:
рут/немота визуальны (существа не ходят/редко кастуют). Стат-эффекты/breaks-on-damage/иммунитеты — позже.
| Тип | Абилки (класс) | Статус |
|---|---|---|
| Stun | Hammer of Justice (Пал ✅), Concussion Blow (Воин), Cheap/Kidney Shot (Разб), Deep Freeze (Маг), Bash (Друид), Shadowfury (ЧК) | ✅ одиночная цель (HoJ + Seal of Justice проверены); AoE-стан ⬜ |
| Root | Frost Nova (Маг — AoE!), Entangling Roots (Друид), Freezing Trap (Охот), Chains of Ice (DK) | 🟡 одиночная цель — визуал; AoE (Frost Nova) ⬜ |
| Fear | Psychic Scream (Жрец — AoE), Fear/Howl of Terror (ЧК), Intimidating Shout (Воин), Scare Beast (Охот) | 🟡 одиночная цель (стопает + визуал); AoE/fleeing ⬜ |
| Silence | Strangulate (DK), Silencing Shot (Охот), Arcane Torrent (Эльф крови); Counterspell-lockout (Маг) | 🟡 (визуал) / ⬜ (lockout) |
| Disorient/Poly | Polymorph (Маг ✅), Blind (Разб), Dragon's Breath (Маг — AoE), Scatter Shot (Охот), Hibernate | ✅ одиночная цель (Polymorph проверен; стопает + визуал); break-on-damage ⬜ |

### 5. Interrupt — ✅ (INT.1)
Pummel/Shield Bash (Воин), Kick (Разб), Counterspell (Маг), Mind Freeze (DK), Wind Shear (Шам), Skull Bash (Друид), Spell Lock (пет ЧК).
Детект эффекта 68 (INTERRUPT_CAST) → прерывание каста цели-существа (SMSG_SPELL_FAILURE гасит каст-бар) + лок школы
на длительность из DurationIndex (Kick 5с/Counterspell 8с/Pummel 4с/Wind Shear 2с). Проверочный стенд — кастующий
манекен (`.dummy caster`, крутит Frostbolt). Todo: боевой спелл-AI существ (урон по игроку), лок школы у игрока (PvP).

### 6. Dispel / Purge / Spellsteal — ✅ (DSP.1–DSP.2)
Cleanse (Пал), Dispel/Cure (Жрец), Remove Curse/Abolish (Маг/Друид), Spellsteal (Маг), Purge (Шам), Cleanse Spirit (Шам).
Тип диспела ауры — `spell_template.Dispel` (1=Magic/2=Curse/3=Disease/4=Poison); снимаемые типы диспел-спелла —
`EffectMiscValue` эффектов 38 (Spellsteal — эффект 126). **DSP.1 защитный:** снять свой дебафф нужного типа
(Cleanse/Remove Curse/Dispel Magic), стенд — `.debuff <spellId>`. **DSP.2 атакующий:** Purge снимает бафф врага,
Spellsteal крадёт Magic-бафф на себя; стенд — кастующий манекен с Magic-баффом (Arcane Intellect). Один диспел =
одна аура (mass dispel — todo). Todo: диспел дружественных игроков (PvP/группа), резист диспела.

### 7. Procs / триггер-спеллы — ✅ (PROC.1 + PROC.2)
Hot Streak/Brain Freeze (Маг), Eclipse (Друид), Sudden Death (Воин), Lava Surge (Шам), Killing Machine (DK).
**PROC.1:** прок-аура (аура 42 PROC_TRIGGER_SPELL) с `procFlags`/`procChance` → на событии перебираем активные
прок-ауры, ролим шанс, накладываем триггер-спелл. Хуки: мили-свинг (DEAL_MELEE_SWING) + вредный каст
(DEAL_HARMFUL_SPELL). **PROC.2 (крит-проки):** `spell_proc_event` уточняет события/условия — `procEx`
PROC_EX_CRITICAL_HIT требует крит триггера, `SchoolMask` фильтрует школу, `procFlags` оттуда переопределяют
шаблон. Прок «вредный спелл» шлётся из `ApplyDamageAsync` (с крит+школа). Покрывает chance-on-melee/on-cast
(Sudden Death) и крит-проки (Elemental Focus → Clearcasting). **Todo:** ICD прока (`Cooldown`), `SpellFamilyMask`
(точный матчинг), мили-крит-проки (нужен мили-крит), скриптовые (Hot Streak — счётчик/dummy), эффект триггер-баффов на след. каст.

### 7a. Спелл-крит — ✅ (CRIT.1)
Крит урона/хила заклинаний ×1.5 (множитель CMaNGOS) + флаг крита в логах (`SPELL_HIT_TYPE_CRIT 0x02` в
`SMSG_SPELLNONMELEEDAMAGELOG`; байт `critical` в `SMSG_SPELLHEALLOG`) → клиент рисует крит. Шанс — флэт
`SessionCastState.SpellCritChance` (база 0; дев-команда `.setcrit [0-100]`). **Todo:** крит из статов
(интеллект/крит-рейтинг — стат-модель упрощена), крит-проки (procEx поверх PROC.1), крит DoT-тиков, мили-крит ×2.

### 8. On-next-hit / оружейные чары / on-hit
Heroic Strike/Cleave/Maul (next-melee), яды разбойника, оружейные имбу шамана, печати паладина (on-hit). Статус: ⬜/🟡.
Печати паладина (on-hit прок holy/хил/мана) — 🟡 (`SealService`, хук в `PlayerMeleeService.TickMeleeAsync`); остальное ⬜.

### 9. Движение/телепорт — частично ЕСТЬ (`SpellMovement`)
Charge (Воин) ✅, Shadowstep (Разб) 🟡, Blink (Маг) 🟡; ⬜: Intercept, Heroic Leap, Death Grip (pull цели), Feral Charge.

### 10. Угроза / таунт (PvE) — ➖
Taunt (Воин), Hand of Reckoning (Пал), Growl (пет), Righteous Fury. Вне текущего этапа.

### 11. Петы / тотемы / призывы — ➖ (самое крупное, в конце)
Питомцы охотника, демоны ЧК, гуль/горгулья/армия DK, тотемы+элементали шамана, элементаль воды/Mirror Image мага,
волки шамана (Feral Spirit), энты друида.

---

## Порядок работы (рекомендация)

1. **Фаза 1**: `.spelltest run` по классам → закрыть 🟡 в уроне/хиле/DoT/HoT.
2. **Фаза 2** сверху вниз: формы/toggle → ресурсы (combo/руны) → митигейшн/absorb → CC → interrupt/dispel → procs → петы.
3. Чиня механику — прогонять её у ВСЕХ классов из списка выше, отмечать статус в `<class>-abilities.md` + здесь.

Эталон формул/типов аур — `C:\repo\mangos-wotlk` (`src/game/Spells/`, `SpellAuraDefines.h`).
