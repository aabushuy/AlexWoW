# Воин — абилки (WoW 3.3.5a)

Ресурс — **ярость**. Стойки — эксклюзивные toggle-формы (группа `GroupShapeshift`). Легенда/колонки — [README](README.md).

## Общее

| Абилка | Спелл | Школа/Аура | Тип | Статус | Заметки |
|---|---|---|---|---|---|
| Боевой клич (Battle Shout) | 6673 | MOD_ATTACK_POWER (буфф) | Buff | 🟡 | групповой бафф AP |
| Боевая стойка (Battle Stance) | 2457 | форма 17 | Toggle | ✅ | группа стоек |
| Защитная стойка (Defensive Stance) | 71 | форма 18 | Toggle | ✅ | −урон/+угроза |
| Стойка берсерка (Berserker Stance) | 2458 | форма 19 | Toggle | ✅ | +крит/−защита |
| Рывок (Charge) | 100 | Physical | Utility+Melee | ✅ | сплайн + генерация ярости (M10.6) |
| Героический удар (Heroic Strike) | 78 | Physical | Melee | 🟡 | next-melee; flat-бонус сверен харнесом #3 (правило BelowExpected) |
| Раздробить (Rend) | 772 | Physical | DoT | ✅ | тик сверен харнесом #3 (5/8/76 = эталон) |
| Героический бросок (Heroic Throw) | 57755 | Physical | Ranged | ✅ | урон 12 сверен харнесом #3 |
| Сокрушающий бросок (Shattering Throw) | 64382 | Physical | Ranged+Dispel | ✅ | урон 12 сверен #3; снятие иммунитета — отдельно |
| Подрезать (Hamstring) | 1715 | Physical | Melee+Debuff | ⬜ | замедление (MOD_DECREASE_SPEED) |
| Разить (Cleave) | 845 | Physical | Melee (AoE) | ⬜ | |
| Удар грома (Thunder Clap) | 6343 | Physical | Melee (AoE)+Debuff | ✅ | урон 15/300 сверен харнесом #3; дебафф MOD_MELEE_HASTE− — отдельно |
| Деморализующий крик (Demoralizing Shout) | 1160 | MOD_ATTACK_POWER− | Debuff | ⬜ | |
| Добивание (Execute) | 5308 | Physical | Melee | ⬜ | только <20% HP |
| Удар возмездия (Victory Rush) | 34428 | Physical | Melee | ✅ | урон 45 сверен харнесом #3 |
| Ярость берсерка (Berserker Rage) | 18499 | MECHANIC_IMMUNITY (буфф) | Buff | ⬜ | |
| Провокация (Taunt) | 355 | MOD_THREAT | Utility | ➖ | угроза (PvE-агро) |
| Оглушающий удар (Pummel) | 6552 | Physical | Interrupt | ⬜ | прерывание (стойка берсерка) |
| Удар щитом (Shield Bash) | 72 | Physical | Interrupt | ⬜ | требует щит |
| Сотрясение (Concussion Blow) | 12809 | Physical | Melee+Stun | ⬜ | талант Prot |
| Разоружение (Disarm) | 676 | MOD_DISARM | Debuff | ⬜ | |
| Устрашающий крик (Intimidating Shout) | 5246 | Fear | CC | ⬜ | |
| Кровавая ярость (Bloodrage) | 2687 | ENERGIZE (ярость) | Utility | 🟡 | генерация ярости |

## Оружие/Защита (база разных стоек)

| Абилка | Спелл | Школа/Аура | Тип | Статус | Заметки |
|---|---|---|---|---|---|
| Раскол брони (Sunder Armor) | 7386 | MOD_RESISTANCE− | Debuff | ⬜ | стак брони− |
| Превозмогание (Overpower) | 7384 | Physical | Melee | ⬜ | после уклонения цели |
| Расплата (Revenge) | 6572 | Physical | Melee | ⬜ | после блока/парир/уклон |
| **Блок щитом (Shield Block)** | 2565 | **MOD_BLOCK_PERCENT (51)** | Buff | ✅ | +% блока (этой сессии) |
| **Глухая оборона (Shield Wall)** | 871 | **MOD_DAMAGE_PERCENT_TAKEN (87)** | Buff | ✅ | −% получаемого урона (этой сессии) |
| Отражение заклинаний (Spell Reflection) | 23920 | REFLECT_SPELLS | Buff | ⬜ | требует щит |
| Вихрь (Whirlwind) | 1680 | Physical | Melee (AoE) | ⬜ | стойка берсерка |
| Перехват (Intercept) | 20252 | Physical | Utility+Melee | ⬜ | charge берсерка |

## Арсенал специализаций (даются талантами — дублируются в `<...>-talents.md`)

| Абилка | Спелл | Дерево | Тип | Статус |
|---|---|---|---|---|
| Смертельный удар (Mortal Strike) | 12294 | Arms | Melee+Heal− | ⬜ |
| Жажда крови (Bloodthirst) | 23881 | Fury | Melee | ⬜ |
| Удар щитом (Shield Slam) | 23922 | Protection | Melee+Dispel | ⬜ |
| Опустошение (Devastate) | 20243 | Protection | Melee+Sunder | ⬜ |
| Размашистые удары (Sweeping Strikes) | 12328 | Arms | Buff | ⬜ |

> **Что в первую очередь чинить:** распознать в `SpellCatalog` типы аур замедления/брони/угрозы и interrupt-эффекты.
> Базовые мили (Heroic Strike/Rend) — через `EffectWeaponDamage`/периодику; защитные ауры — по образцу Shield Block/Wall.
