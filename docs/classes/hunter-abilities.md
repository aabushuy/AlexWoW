# Охотник — абилки (WoW 3.3.5a)

Ресурс — **мана** (+ фокус пета). Аспекты — эксклюзивные toggle (`GroupHunterAspect`). Дальний бой/пет.
Легенда/колонки — [README](README.md).

## Общее

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Аспект ястреба (Aspect of the Hawk) | 13165 | toggle / MOD_ATTACK_POWER | Toggle | ✅ |
| Аспект гепарда (Aspect of the Cheetah) | 5118 | toggle / MOD_SPEED | Toggle | ✅ |
| Аспект обезьяны (Aspect of the Monkey) | 13163 | toggle / уклонение | Toggle | ✅ |
| Аспект дракона (Aspect of the Dragonhawk) | 61846 | toggle | Toggle | 🟡 |
| Автовыстрел (Auto Shot) | 75 | Physical (ranged) | Ranged | ⬜ |
| Метка охотника (Hunter's Mark) | 1130 | MOD_RANGED_ATTACK_POWER (debuff) | Debuff | ⬜ |
| Отпугивание (Scare Beast) | 1513 | Fear (звери) | CC | ➖ |
| Замораживающая ловушка (Freezing Trap) | 1499 | root/freeze | CC | ⬜ |
| Взрывающаяся ловушка (Explosive Trap) | 13813 | Fire | DoT (земля) | ⬜ |
| Отвлечение (Feign Death) | 5384 | сброс агро | Utility | ➖ |
| Зов питомца / Приручение | 883/1515 | пет | Summon | ➖ |

## Повелитель зверей (Beast Mastery)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Натиск зверя (Bestial Wrath) | 19574 | пет-бафф | Buff | ➖ |
| Усмирение питомца (Intimidation) | 19577 | stun (пет) | CC | ➖ |

## Стрельба (Marksmanship)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Прицельный выстрел (Aimed Shot) | 19434 | Physical (ranged) | Ranged | ⬜ |
| Стабилизирующий выстрел (Steady Shot) | 56641 | Physical (ranged) | Ranged | 🟡 |
| Залп (Multi-Shot) | 2643 | Physical (ranged, AoE) | Ranged | ⬜ |
| Магический выстрел (Arcane Shot) | 3044 | Arcane | Ranged | 🟡 |
| Быстрое прицеливание (Rapid Fire) | 3045 | MOD_RANGED_HASTE | Buff | ⬜ |
| Залп стрел (Volley) | 1510 | Arcane | Channel (AoE) | ⬜ |

## Выживание (Survival)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Выстрел в упор (Explosive Shot) | 53301 | Fire | DoT (ranged) | 🟡 |
| Удар по змее (Serpent Sting) | 1978 | Nature | DoT | 🟡 |
| Метание ножей (Wing Clip) | 2974 | Physical + замедление | Melee | ⬜ |
| Удар мангусты (Mongoose Bite) | 1495 | Physical | Melee | ⬜ |
| Дезориентирующий выстрел (Scatter Shot) | 19503 | disorient | CC | ⬜ |

> **Чинить:** ranged-абилки = `EffectWeaponDamage`/ranged-урон (мили-логика есть, ranged — проверить); DoT уже умеем;
> ловушки/аспекты — toggle/земля-аура; пет-механики — вне этапа (➖).
