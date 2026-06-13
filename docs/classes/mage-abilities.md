# Маг — абилки (WoW 3.3.5a)

Ресурс — **мана**. Школы: Arcane/Fire/Frost. Брони мага (Frost/Mage/Molten) — эксклюзивные toggle-ауры.
Легенда/колонки — [README](README.md).

## Общее

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Превращение (Polymorph) | 118 | Arcane / CC | CC | ✅ дезориентация одиночной цели + визуал (CC-фреймворк); break-on-damage ⬜ |
| Контрзаклинание (Counterspell) | 2139 | Arcane / interrupt+lockout | Interrupt | ⬜ |
| Кража заклинания (Spellsteal) | 30449 | Arcane / dispel-steal | Utility | ⬜ |
| Снятие проклятия (Remove Curse) | 475 | Arcane / dispel | Utility | ⬜ |
| Мерцание (Blink) | 1953 | Arcane / телепорт вперёд | Utility | 🟡 (движение есть) |
| Невидимость (Invisibility) | 66 | MOD_INVISIBILITY | Buff | ⬜ |
| Чародейский интеллект (Arcane Intellect) | 1459 | MOD_STAT (инт) | Buff | 🟡 |
| Ледяная броня (Frost Armor) | 168 | MOD_RESISTANCE/щит | Toggle | ✅ эксклюзив проверен вживую; стат не моделируется |
| Магическая броня (Mage Armor) | 6117 | MOD_POWER_REGEN | Toggle | ✅ эксклюзив проверен вживую; стат не моделируется |
| Расплавленная броня (Molten Armor) | 30482 | MOD_CRIT/щит | Toggle | ✅ эксклюзив + наложение (фикс прок-брони) проверены; стат не моделируется |
| Чародейский щит (Mana Shield) | 1463 | MANA_SHIELD | Buff | ✅ поглощает урон за счёт маны (ABS.2: 1.5 маны/ед.), спадает при исчерпании пула |
| Магический дар (Conjure Water/Food/Gem) | 5504/587/759 | CreateItem | Utility | 🟡 (CreateItem есть) |
| Телепорт/Портал | 3561… | Utility / телепорт | Utility | ⬜ |

## Тайная магия (Arcane)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Тайные стрелы (Arcane Missiles) | 5143 | Arcane | DoT-канал | 🟡 |
| Чародейский взрыв (Arcane Explosion) | 1449 | Arcane | Direct (AoE) | ✅ урон сверен #4 |
| Чародейская вспышка (Arcane Blast) | 30451 | Arcane | Direct | 🟡 |
| Тайный обстрел (Arcane Barrage) | 44425 | Arcane | Direct | ✅ урон сверен #4 |
| Концентрация (Presence of Mind) | 12043 | Buff (мгн. каст) | Buff | ⬜ |
| Сила тайной магии (Arcane Power) | 12042 | MOD_DAMAGE_PERCENT_DONE | Buff | ⬜ |
| Чародейское озарение (Evocation) | 12051 | ENERGIZE (мана) | Channel | ⬜ |

## Огонь (Fire)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Огненная стрела (Fireball) | 133 | Fire | Direct + DoT | ✅ урон+DoT сверен #4 |
| Огненный взрыв (Fire Blast) | 2136 | Fire | Direct (мгн.) | ✅ урон сверен #4 |
| Опаление (Scorch) | 2948 | Fire | Direct | ✅ урон сверен #4 |
| Огненный столб (Flamestrike) | 2120 | Fire | Direct (AoE) + DoT | ✅ урон сверен #4 (DoT-тик не пойман) |
| Пиробласт (Pyroblast) | 11366 | Fire | Direct + DoT | ✅ урон+DoT сверен #4 |
| Ледофайр (Frostfire Bolt) | 44614 | Frostfire (Fire+Frost) | Direct + DoT | ✅ урон+DoT сверен #4 |
| Горящая бомба (Living Bomb) | 44457 | Fire | DoT + взрыв | ✅ DoT-тик сверен #4 (взрыв отдельно) |
| Взрывная волна (Blast Wave) | 11113 | Fire | Direct (AoE)+CC | ✅ урон сверен #4 |
| Возгорание (Combustion) | 11129 | Buff (крит) | Buff | ⬜ |
| Драконье дыхание (Dragon's Breath) | 31661 | Fire / disorient | Direct+CC | ✅ урон сверен #4 (CC отдельно) |

## Лёд (Frost)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Ледяная стрела (Frostbolt) | 116 | Frost | Direct + замедление | ✅ урон сверен #4 (замедл. отдельно) |
| Ледяное копьё (Ice Lance) | 30455 | Frost | Direct | ✅ урон сверен #4 |
| Ледяные оковы (Frost Nova) | 122 | Frost / root (AoE) | Direct+CC | ✅ урон сверен #4 (root отдельно) |
| Метель (Blizzard) | 10 | Frost | DoT-канал (AoE) | 🟡 |
| Конус холода (Cone of Cold) | 120 | Frost | Direct (конус)+замедл. | ✅ урон сверен #4 (замедл. отдельно) |
| Ледяная преграда (Ice Barrier) | 11426 | SCHOOL_ABSORB | Buff (щит) | ✅ поглощает входящий урон (ABS.1), щит спадает при исчерпании пула |
| Глубокая заморозка (Deep Freeze) | 44572 | Frost / stun | Direct+CC | ⬜ |
| Ледяная глыба (Ice Block) | 45438 | SCHOOL_IMMUNITY | Buff | ⬜ |
| Ледяные вены (Icy Veins) | 12472 | MOD_CASTING_SPEED | Buff | ⬜ |
| Призыв элементаля воды | 31687 | призыв пета | Summon | ➖ |

> **Чинить:** direct/DoT-урон уже data-driven (🟡 → проверить клиентом). Особые механики — CC (root/stun/poly),
> абсорб-щиты (SCHOOL_ABSORB), иммунитеты (Ice Block), брони-тоглы, interrupt+lockout (Counterspell).
