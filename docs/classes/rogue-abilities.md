# Разбойник — абилки (WoW 3.3.5a)

Ресурс — **энергия** + **очки серии (combo points)**. Школа — Physical (+ яды Nature). Скрытность.
Легенда/колонки — [README](README.md).

## Общее

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Скрытность (Stealth) | 1784 | форма 30 / MOD_STEALTH | Toggle | 🟡 toggle-форма (ранги 1784-1787); стелс-механика не моделируется |
| Бесшумность (Vanish) | 1856 | invis+стелс | Utility | ⬜ |
| Отвлекающий удар (Distract) | 1725 | Utility | Utility | ⬜ |
| Ослепление (Blind) | 2094 | disorient | CC | ⬜ |
| Удар ниже пояса (Kick) | 1766 | interrupt | Interrupt | ⬜ |
| Финт (Feint) | 1966 | MOD_THREAT− | Utility | ➖ |
| Уклонение (Evasion) | 5277 | MOD_DODGE_PERCENT | Buff | ⬜ (как dodge-бафф) |
| Спринт (Sprint) | 2983 | MOD_SPEED | Buff | ⬜ |
| Вскрытие замков (Pick Lock) | 1804 | Utility | Utility | ➖ |
| Яды (Instant/Deadly Poison) | 8679/2823 | Nature / on-hit | Buff(оружие) | ⬜ |

## Ликвидация (Assassination)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Скрытый удар (Ambush) | 8676 | Physical (из стелса) | Melee+CP | ⬜ |
| Подлый приём (Cheap Shot) | 1833 | stun (из стелса) | CC+CP | ⬜ |
| Гарота (Garrote) | 703 | Physical | DoT+CP | 🟡 |
| Мутиляция (Mutilate) | 1329 | Physical | Melee+CP | ⬜ |
| Вскрытие (Rupture) | 1943 | Physical | DoT (расход CP) | 🟡 |
| Удар в спину (Backstab) | 53 | Physical (со спины) | Melee+CP | ⬜ |

## Бой (Combat)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Скрытный удар (Sinister Strike) | 1752 | Physical | Melee+CP | 🟡 |
| Серия ударов (Eviscerate) | 2098 | Physical | Melee (расход CP) | 🟡 |
| Веер ножей (Fan of Knives) | 51723 | Physical (AoE) | Melee | ⬜ |
| Удар по почкам (Kidney Shot) | 408 | stun (расход CP) | CC | ⬜ |
| Жажда крови (Adrenaline Rush) | 13750 | ENERGIZE/haste | Buff | ⬜ |
| Лезвийный вихрь (Blade Flurry) | 13877 | MOD_MELEE_HASTE | Buff | ⬜ |

## Скрытность (Subtlety)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Танец теней (Shadow Dance) | 51713 | Buff (стелс-абилки) | Buff | ⬜ |
| Шаг сквозь тень (Shadowstep) | 36554 | телепорт за спину | Utility | 🟡 (движение есть) |
| Приготовление (Preparation) | 14185 | сброс КД | Utility | ⬜ |
| Скверный трюк (Hemorrhage) | 16511 | Physical | Melee+CP | ⬜ |

> **Чинить:** очки серии (combo points) как ресурс — НЕТ (нужно завести); энергия — есть; стелс/яды — новые механики;
> мили+CP-абилки — на базе мили-урона; Evasion/Blade Flurry — dodge/haste-ауры (умеем процент-ауры).
