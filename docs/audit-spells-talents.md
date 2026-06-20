# Аудит спеллов и талантов: AlexWoW vs CMaNGOS-WoTLK

> Source-of-truth для эпика **«Спеллы и таланты — gap-port»** в проекте *Порт CMaNGOS*.
> Эталон: `/data/wow-project/repos/mangos-wotlk/` (homeserver).
> Снято: 2026-06-20.

## TL;DR

- **SPELL_EFFECT_*:** у нас ~17 из 164 (10%). Остальные 147 — большая часть стабовых, но «значащих» нет‑тики ~60.
- **SPELL_AURA_*:** у нас ~30 из 317 (9%). Большие зияющие пробелы: hit/crit/haste rating, dummy/override-class-scripts (база талантов с сценарной логикой), стелс/детект, threat-механика, periodic-trigger, raid-proc-from-charge.
- **Class-scripts:** реализованы куски (SealService/RuneService/PoisonService/ImbueService/ComboPointService), но 9 из 10 class-files CMaNGOS не покрыты целиком. Особенно зияет Mage (Ignite/Shatter/Clearcasting), Warlock (UA/CoD/SoC/Healthstone), Priest (Lightwell/PoM/Shadowfiend), Hunter (Misdirection/Kill Command/MfD), DK (AMZ/Icebound/Obliterate runes).
- **Талант-движок:** SpellModifierService покрывает ауры **107/108** с **10 из ~30** SpellModOp. Полностью нет: `SPELL_AURA_DUMMY (4)`, `OVERRIDE_CLASS_SCRIPTS (112)`, `RAID_PROC_FROM_CHARGE (223/225)`, **глифов (Effect 147)**, **dual spec (156/157)**.

---

## 1. SPELL_EFFECT_*

### 1.1 Покрыто у нас

| ID | Эффект | Где |
|---|---|---|
| 2 | SchoolDamage | SpellEffectsService.ApplyDamageAsync |
| 3 | Dummy (узкий) | только ярость Рывка (через семейство Warrior + Charge) |
| 5 | TeleportUnits | SpellEffectsService.ApplyTeleportAsync (Shadowstep) |
| 6 | ApplyAura | PeriodicsService + AuraService (но ауры — см. §2) |
| 10 | Heal | SpellEffectsService.ApplyHealAsync |
| 17/58/121 | WeaponDamage* | через info.WeaponDamage |
| 24 | CreateItem | CraftingService |
| 29 | Leap | SpellEffectsService.ApplyTeleportAsync (Blink) |
| 30 | Energize | через info.EnergizeAmount/Power |
| 31 | WeaponPercentDamage | через info.WeaponPercent |
| 38 | Dispel | DispelService |
| 54 | EnchantItemTemporary | ImbueService/PoisonService |
| 64 | TriggerSpell | SpellCastCompletion (цепочки тип Shadowstep) |
| 68 | InterruptCast | через info.IsInterrupt |
| 80 | AddComboPoints | ComboPointService |
| 96 | Charge | SpellEffectsService.ApplyChargeAsync |
| 126 | StealBeneficialBuff | DispelService (Spellsteal) |

### 1.2 Не покрыто (приоритетное)

**Тоталем 60+ значащих эффектов CMaNGOS, разнесённых по группам:**

- **Pet/Summon:** SummonPet (56), SummonAllTotems (149), DismissPet (47), TameCreature (44), CreateTamedPet (152), RenamePet (161), FeedPet (45), SummonObject* (50/76/104/105/107). Частично есть в Pet/PetRegistry, но эффект-уровень не интегрирован.
- **Threat/Taunt:** Taunt (90), Threat (53), ModifyThreatPercent (124), RedirectThreat (139). Танк-механика — основа для подземелий.
- **Resurrect:** Resurrect (18), SelfResurrect (95), Resurrect (на корпус), SpiritHeal (97).
- **Healing variants:** HealMaxHealth (75), HealPct (136), EnergisePct (137), HealMechanical (62).
- **Power vampirism:** PowerDrain (8), PowerBurn (53), HealthLeech (9).
- **Movement:** Jump (145), LeapBack (151), KnockBack (98), KnockBackFromPosition (158), Pull (70), PullTowards (138), Distract (74).
- **Area auras:** PersistentAreaAura (27), ApplyAreaAura (35) — основа группой/рейдовых баффов (Auras of Paladin, Totems).
- **Glyphs / Dual spec:** ApplyGlyph (147), ActivateSpec (156), SpecCount (157) — полностью нет.
- **Учительные:** LearnSpell (36), LearnSkill (40), LearnPetSpell (57). Сейчас выдаём через сервис, но через эффект — нет.
- **OpenLock / Skinning / DisEnchant / Prospecting / Milling / PickPocket:** профессии и взаимодействие с объектами.
- **Misc:** Bind (32), AddHonor (47?), Reputation (103), Stuck (147), ActivateObject (95), Sanctuary (123), Duel (114), Inebriate (115), Spawn (108), ScriptEffect (77), SendEvent (61), Skinning (66), Skill (158), KillCredit*.
- **Stubs CMaNGOS:** 4, 12-15, 20-21, 25-26, 37-39, 48-49, 51-52, 78, 81, 91, 112, 122, 163 — НЕ портируем (так и в эталоне пусты).

---

## 2. SPELL_AURA_*

### 2.1 Покрыто у нас (~30)

```
PERIODIC_DAMAGE(3), PERIODIC_HEAL(8), PERIODIC_ENERGIZE(24), PERIODIC_LEECH(53→как DoT)
MOD_CONFUSE(5), MOD_FEAR(7), MOD_STUN(12), MOD_ROOT(26), MOD_SILENCE(27), MOD_SHAPESHIFT(36)
MOD_STAT(29), MOD_INCREASE_HEALTH(34), MOD_INCREASE_SPEED(31)
SCHOOL_IMMUNITY(39), DAMAGE_IMMUNITY(40), MECHANIC_IMMUNITY(77)
PROC_TRIGGER_SPELL(42), PROC_TRIGGER_DAMAGE(43)
MOD_DODGE_PERCENT(49), MOD_BLOCK_PERCENT(51), MOD_ATTACKER_MELEE_HIT_CHANCE(184)
MOD_ATTACK_POWER(99), MOD_RANGED_ATTACK_POWER(124)
MOD_DAMAGE_PERCENT_TAKEN(87), MOD_DAMAGE_PERCENT_DONE(79)
SCHOOL_ABSORB(69), MANA_SHIELD(97)
ADD_FLAT_MODIFIER(107), ADD_PCT_MODIFIER(108)
```

### 2.2 Зияющие пробелы (приоритетные)

**Хит/крит/хейст-рейтинги — самое больное (без них таланты и предметы «не работают»):**
- `MOD_HIT_CHANCE (54)`, `MOD_SPELL_HIT_CHANCE (55)`, `MOD_SPELL_CRIT_CHANCE (57)`, `MOD_CRIT_PERCENT (52)`, `MOD_PARRY_PERCENT (47)`, `MOD_RESISTANCE (22)`
- `MOD_MELEE_HASTE (138/217)`, `MOD_RANGED_HASTE (140)`, `HASTE_SPELLS (216)`, `HASTE_ALL (193)`
- `MOD_RATING (189)` — общий механизм combat ratings (cap по уровню → %)

**Скриптовая база талантов (огромный пласт):**
- `SPELL_AURA_DUMMY (4)` — per-spellId handler regsitry (TalentX → custom hook)
- `SPELL_AURA_OVERRIDE_CLASS_SCRIPTS (112)` — to же
- `PERIODIC_DUMMY (226)`, `PERIODIC_TRIGGER_SPELL (23)`, `PERIODIC_TRIGGER_SPELL_WITH_VALUE (227)`
- `PROC_TRIGGER_SPELL_WITH_VALUE (231)`
- `RAID_PROC_FROM_CHARGE (223)`, `RAID_PROC_FROM_CHARGE_WITH_VALUE (225)` — Prayer of Mending, Beacon-like

**Стелс / Невидимость / Детект:**
- `MOD_STEALTH (16)`, `MOD_STEALTH_DETECT`, `MOD_INVISIBILITY (18)`, `MOD_INVISIBILITY_DETECTION (19)`, `MOD_DETECT (17)`

**Threat / Аггро:**
- `MOD_TOTAL_THREAT (145)`, `MOD_TAUNT (146)`, `MOD_DETAUNT`, `FORCE_REACTION (213)`

**Регены/доп. ресурсы:**
- `MOD_REGEN (84)`, `MOD_POWER_REGEN (85)`, `MOD_MANA_REGEN_FROM_STAT (219)`, `MOD_REGEN_DURING_COMBAT (91)`
- `MOD_INCREASE_HEALTH_PERCENT (133)`, `MOD_INCREASE_ENERGY_PERCENT (132)`
- `MOD_PERCENT_STAT (106)`, `MOD_TOTAL_STAT_PERCENTAGE (137)`

**Хил/Урон-моды:**
- `MOD_HEALING_DONE (135)`, `MOD_HEALING_PCT (64)`, `MOD_DAMAGE_DONE (13)`, `MOD_DAMAGE_TAKEN (14)`
- `MOD_DAMAGE_PCT_DONE_VS_AURA_STATE` — Sword & Board-подобные триггеры урона по состоянию

**Сопротивления и спец-резисты:**
- `MOD_RESISTANCE_PCT`, `MOD_BASE_RESISTANCE_PCT`, `MOD_RESISTANCE_EXCLUSIVE`, `MOD_TARGET_RESISTANCE`
- `MOD_DEFENSE_SKILL_PCT`, `MOD_PENETRATION`, `MOD_EXPERTISE (210)`

**Контроль:**
- `MOD_CHARM (6)`, `MOD_PACIFY (25)`, `MOD_PACIFY_SILENCE (24)`, `FEIGN_DEATH (73)`

**Транспорт/миры:**
- `MOUNTED (78)`, `MOD_FLIGHT_SPEED (208/210)`, `WATER_BREATHING`, `MOD_SHAPESHIFT_TRANSFORM`

---

## 3. Class-scripts (Spells/Scripts/ClassScripts/*.cpp)

| Класс | Что есть у нас | Чего не хватает (имённые механики из CMaNGOS) |
|---|---|---|
| **Warrior** | базовый бой, Charge-ярость | **Execute** (BasePoints×CP=ярость), **Victory Rush**, **Sunder Armor**, **Devastate** (бонус за стаки Sunder), **Retaliation**, **Vigilance** (танк-аура), **Spell Reflection**, **Intervene**, **Glyph of Victory Rush** |
| **Paladin** | SealService (печати), Judgement-каркас | **Righteous Defense**, **Divine Storm**, **Judgements of the Wise**, **Sacred Shield**, **Exorcism** vs undead/demon, **Blessing of Sanctuary** (proc), **Hand of Salvation**, **Divine Intervention**, **Forbearance** (есть в каталоге, но не интегрирован полностью с КД пузырей) |
| **Hunter** | — | **Kill Command**, **Misdirection** (threat redirect), **Marked For Death**, **Entrapment**, **Steady Shot glyph**, **Trueshot Aura glyph**, **Expose Weakness**, **Arcane Shot** (consume magic) |
| **Rogue** | PoisonService, ComboPointService | **Preparation** (reset cooldowns), **Vanish**, **Sap**, **Setup** (proc dodge → CP), **Cheat Death**, **Killing Spree**, **Nerves of Steel**, **Hemorrhage** (debuff stacks), **Stealth/Prowl** позиционные бонусы |
| **Priest** | — | **Power Word: Shield** (мы покрываем Absorb, но не Weakened Soul), **Reflective Shield**, **Prayer of Mending** (raid_proc_from_charge), **Lightwell** (saved spawn), **Spirit of Redemption**, **Power Infusion**, **Shadow Word: Death** (backlash), **Shadowfiend** (mana return), **Pain Suppression**, **Twisted Faith**, **Guardian Spirit**, **Divine Hymn** |
| **DK** | RuneService (руны) | **Crypt Fever**, **Spell Deflection**, **Anti-Magic Zone** (50461/50462), **Anti-Magic Shell** (school absorb с типом), **Merciless Combat**, **Tundra Stalker**, **Rage of Rivendare**, **Glacier Rot**, **Icebound Fortitude** (defense-based), **Obliterate** (rune+disease consumption) |
| **Druid** | формы, имбы базовые | **Mangle** (Cat/Bear stance branch), **Shred** (требует «за спиной»), **Force of Nature** (treants), **Primal Tenacity**, **Starfire Bonus**, **Nourish**, **Typhoon**, **Thorns** (proc damage on melee), **Regrowth + Rejuvenation** HoT-стак, **Moonkin** автоатак |
| **Mage** | — | **Arcane Concentration / Clearcasting** (proc), **Shatter** (crit vs frozen), **Ignite** (DoT из крита Fire), **Molten Fury**, **Ice Lance** (×3 vs frozen), **Fingers of Frost**, **Deep Freeze**, **Polymorph** (eat heal), **Fire/Frost Ward** (school absorb с проком), **Blast Wave**, **Mirror Image** |
| **Warlock** | — | **Unstable Affliction** (silence proc на dispel), **Curse of Agony** (растущий DoT), **Curse of Doom**, **Inferno**, **Life Tap**, **Create Healthstone**, **Demonic Knowledge** (pet→AP), **Demonic Sacrifice** (pet→buff), **Devour Magic**, **Seed of Corruption** (AoE explode), **Soul Leech**, **Demonic Circle** (teleport), **Drain Life/Mana** (leech mechanics) |
| **Shaman** | ImbueService (weapon imbues) | **Totems** (5730 Stoneclaw, 8190 Magma, 39610 Mana Tide, 52041 Healing Stream, 5394 Healing Stream и пр.) — **полностью**, **Sentry Totem**, **Earth Shield** (proc heal на цели), **Fire Nova**, **Astral Shift**, **Lava Burst**, **Rockbiter Weapon**, **Reincarnation** |

---

## 4. Талант-движок

### 4.1 Реализовано

- `TalentMath` — формула MaxPoints по классу/уровню (CMaNGOS-точная).
- `TalentHandlers` — `CMSG_LEARN_TALENT` (валидация: класс↔дерево, ранг, тир-гейт, пререквизит) + `MSG_TALENT_WIPE_CONFIRM` (сброс за золото 1g→5g→10g→…→50g, с возвратом ранг-спеллов).
- `SpellModifierService` + `SpellModifiers` — пассивные таланты с аурами **107/108** через семейство + 96-битную маску. **SpellModOp:** `Damage / Duration / CastingTime / Cooldown / Cost / Dot / Effect1 / Effect2 / Effect3 / AllEffects` (10 операций).

### 4.2 Не реализовано

- **Остальные SpellModOp (~20):** `CritChance (5)`, `RadiusMod`, `GlobalCooldown`, `Charges`, `JumpTargets`, `CriticalDamage`, `ProcChance`, `BonusMultiplier`, `DamageMultiplier`, `BaseResistance`, `Resist Miss Chance`, `Range`, `Activation Time`, `Threat`, `Block Value`, `Spell Power`, и т.д.
- **`SPELL_AURA_DUMMY (4)`** как **registry per-spellId** (Mage Improved Frostbolt, Warrior Booming Voice, Druid Furor, Paladin Vindication, Hunter Hunter vs Wild, и т.д. — сотни). Сейчас Dummy обрабатывается только в очень узком случае (ярость Рывка).
- **`SPELL_AURA_OVERRIDE_CLASS_SCRIPTS (112)`** — то же.
- **Глифы** (`SPELL_EFFECT_APPLY_GLYPH = 147`, серверный кэш `character_glyphs`, эффект-уровень + DBC `GlyphProperties.dbc`).
- **Dual spec** (`SPELL_EFFECT_TALENT_SPEC_COUNT = 157`, `ACTIVATE_SPEC = 156`, `character_talent` по spec, чтение/запись активной spec).
- **Pet talents** (хантер Beast Mastery второе/третье дерево пета).

---

## 5. Эпик и разбивка

См. эпик «Спеллы и таланты — gap-port» в kanban (проект *Порт CMaNGOS*).
Разбито на ~12 крупных T-тикетов с приоритетом по «фундамент → класс-скрипты».
