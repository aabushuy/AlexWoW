# Разбойник — абилки (WoW 3.3.5a)

Ресурс — **энергия** + **очки серии (combo points)**. Школа — Physical (+ яды Nature). Скрытность.
Легенда/колонки — [README](README.md).

## Общее

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Скрытность (Stealth) | 1784 | форма 30 / MOD_STEALTH | Toggle | ✅ toggle-форма (ранги 1784-1787; выход + вход без релога, cooldown-event); стелс-механика не моделируется |
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
| Вскрытие (Rupture) | 1943 | Physical | DoT (расход CP) | ✅ финишер: гейт+расход CP, тик DoT и длительность скалируются очками (CP.3/CP.3b: 6→16с) |
| Удар в спину (Backstab) | 53 | Physical (со спины) | Melee+CP | ⬜ |

## Бой (Combat)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Скрытный удар (Sinister Strike) | 1752 | Physical | Melee+CP | ✅ генератор: +1 очко серии на цели (CP.2) |
| Серия ударов (Eviscerate) | 2098 | Physical | Melee (расход CP) | ✅ финишер: гейт+расход CP, урон скалируется очками ×370/ранг (CP.3) |
| Веер ножей (Fan of Knives) | 51723 | Physical (AoE) | Melee | ⬜ |
| Удар по почкам (Kidney Shot) | 408 | stun (расход CP) | CC | ✅ финишер: гейт+расход CP, длительность стана от очков (CP.3b: 1→6с) |
| Жажда крови (Adrenaline Rush) | 13750 | ENERGIZE/haste | Buff | ⬜ |
| Лезвийный вихрь (Blade Flurry) | 13877 | MOD_MELEE_HASTE | Buff | ⬜ |

## Скрытность (Subtlety)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Танец теней (Shadow Dance) | 51713 | Buff (стелс-абилки) | Buff | ⬜ |
| Шаг сквозь тень (Shadowstep) | 36554 | телепорт за спину | Utility | 🟡 (движение есть) |
| Приготовление (Preparation) | 14185 | сброс КД | Utility | ⬜ |
| Скверный трюк (Hemorrhage) | 16511 | Physical | Melee+CP | ⬜ |

> **Очки серии (combo points) — ✅ есть** (Фаза 2, CP.1–CP.3b): ресурс на цели (`SMSG_UPDATE_COMBO_POINTS`),
> генераторы (эффект 80) копят, финишеры (биты FINISHING_MOVE_* в AttributesEx) гейтятся «нет очков»
> (fail 78) и расходуют все очки; урон/тик скалируются `EffectPointsPerComboPoint`, а ДЛИТЕЛЬНОСТЬ —
> `base + (max−base) × очки / 5` из SpellDuration.dbc (Slice and Dice 6→21с, Kidney Shot 1→6с, Rupture
> 6→16с). Очки теряются со смертью цели / сменой комбо-цели. Прочее: стелс/яды — новые механики;
> Evasion/Blade Flurry — dodge/haste-ауры (умеем процент-ауры).
