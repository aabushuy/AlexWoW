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
| Брони мага (Frost/Mage/Molten Armor) | Маг | ⬜ |
| Печати (Seal of Righteousness/Light) — on-hit | Паладин | ⬜ |
| Облик Тьмы (Shadowform) | Жрец | ⬜ |
| Формы (Bear/Cat/Travel/Moonkin/Tree) | Друид | 🟡 (каркас форм) |
| Присутствия (Blood/Frost/Unholy) | DK | ⬜ |
| Метаморфоза / Fel-Demon Armor | Чернокнижник | ⬜ |
| Призрачный волк (Ghost Wolf) | Шаман | ⬜ |
| Скрытность (Stealth) | Разбойник | ⬜ |

### 2. Ресурсы — рага/энергия/мана ✅; остальные НЕТ
| Ресурс | Класс | Статус | Зажигает |
|---|---|---|---|
| Combo points | Разбойник, друид-кошка | ⬜ | все финишеры (Eviscerate/Rip/Kidney Shot/Ferocious Bite) |
| Руны + runic power | DK | ⬜ | почти все абилки DK |
| Осколки/камни души | Чернокнижник | ⬜ | призывы/Soulstone/Healthstone-гейт |

### 3. Митигейшн / avoidance / absorb — РАЗОГНАЛИСЬ (блок/Глухая оборона)
| Аура | Абилки (класс) | Статус |
|---|---|---|
| MOD_BLOCK_PERCENT (51) | Блок щитом (Воин) ✅, Holy Shield (Пал) ⬜ | частично |
| MOD_DAMAGE_PERCENT_TAKEN (87) | Глухая оборона (Воин) ✅; Divine Protection (Пал), Pain Suppression/Dispersion (Жрец), Barkskin (Друид), Icebound Fortitude (DK), Shamanistic Rage (Шам) | каркас ✅, остальные ⬜ |
| MOD_DODGE_PERCENT (49) | Evasion (Разб), Quickness-расовый | ⬜ |
| SCHOOL_ABSORB | PW:Shield (Жрец), Ice Barrier (Маг), Mana Shield (Маг), Anti-Magic Shell (DK), Sacred Shield (Пал) | ⬜ |
| SCHOOL_IMMUNITY | Ice Block (Маг), Divine Shield (Пал) | ⬜ |

### 4. CC (контроль) — фреймворк НЕТ
| Тип | Абилки (класс) | Статус |
|---|---|---|
| Stun | Hammer of Justice (Пал), War Stomp (Таурен), Concussion Blow (Воин), Cheap/Kidney Shot (Разб), Deep Freeze (Маг), Shadowfury (ЧК) | ⬜ |
| Root | Frost Nova (Маг), Entangling Roots (Друид), Freezing Trap (Охот), Chains of Ice-snare (DK) | ⬜ |
| Fear | Psychic Scream (Жрец), Fear/Howl of Terror (ЧК), Intimidating Shout (Воин), Scare Beast (Охот) | ⬜ |
| Silence | Counterspell-lockout (Маг), Silencing Shot (Охот), Arcane Torrent (Эльф крови), Strangulate (DK) | ⬜ |
| Disorient/Poly | Blind (Разб), Scatter Shot (Охот), Dragon's Breath (Маг), Polymorph (Маг), Banish/Hibernate | ⬜ |

### 5. Interrupt
Pummel/Shield Bash (Воин), Kick (Разб), Counterspell (Маг), Mind Freeze (DK), Wind Shear (Шам), Skull Bash (Друид), Spell Lock (пет ЧК). Статус: ⬜.

### 6. Dispel / Purge / Spellsteal
Cleanse (Пал), Dispel/Cure (Жрец), Remove Curse/Abolish (Маг/Друид), Spellsteal (Маг), Purge (Шам), Cleanse Spirit (Шам). Статус: ⬜.

### 7. Procs / триггер-спеллы
Hot Streak/Brain Freeze (Маг), Eclipse (Друид), Sudden Death (Воин), Lava Surge (Шам), Killing Machine (DK). Статус: ⬜ (триггерная инфраструктура нужна).

### 8. On-next-hit / оружейные чары / on-hit
Heroic Strike/Cleave/Maul (next-melee), яды разбойника, оружейные имбу шамана, печати паладина (on-hit). Статус: ⬜/🟡.

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
