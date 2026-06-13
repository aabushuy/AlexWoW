# Паладин — абилки (WoW 3.3.5a)

Ресурс — **мана**. Ауры паладина — эксклюзивные toggle (`GroupPaladinAura`); печати/благословения. Школа — Holy.
Легенда/колонки — [README](README.md).

## Общее

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Аура преданности (Devotion Aura) | 465 | toggle / MOD_RESISTANCE | Toggle | ✅ (группа аур) |
| Аура возмездия (Retribution Aura) | 7294 | toggle / dmg-shield | Toggle | ✅ |
| Аура сосредоточения (Concentration Aura) | 19746 | toggle | Toggle | ✅ |
| Благословение Мудрости (Blessing of Wisdom) | 19742 | MOD_POWER_REGEN | Buff | ⬜ |
| Благословение Могущества (Blessing of Might) | 19740 | MOD_ATTACK_POWER | Buff | 🟡 |
| Благословение Королей (Blessing of Kings) | 20217 | MOD_TOTAL_STAT% | Buff | ⬜ |
| Печать праведности (Seal of Righteousness) | 21084 | toggle / on-hit Holy | Toggle | ✅ эксклюзив + on-hit holy-урон по свингу (упрощённо ~уровень; SP/AP не моделируется) |
| Печать света (Seal of Light) | 20165 | toggle / on-hit heal | Toggle | ✅ эксклюзив + on-hit хил по свингу (упрощённо) |
| Печать мудрости (Seal of Wisdom) | 20166 | toggle / on-hit mana | Toggle | ✅ эксклюзив + on-hit мана по свингу (3% макс.) |
| Печать справедливости (Seal of Justice) | 20164 | toggle / on-hit stun-proc | Toggle | ✅ эксклюзив + on-hit стан цели (через CC-фреймворк, прок 20170) |
| Кара (Judgement) | 20271 | Holy | Direct | ⬜ |
| Длань защиты (Hand of Protection) | 1022 | физ. иммунитет | Buff | ⬜ |
| Длань свободы (Hand of Freedom) | 1044 | MECHANIC_IMMUNITY (root) | Buff | ⬜ |
| Очищение (Cleanse) | 4987 | dispel | Utility | ✅ снимает свой дебафф (яд/болезнь) — DSP.1 |
| Возложение рук (Lay on Hands) | 633 | Holy | Direct heal | ⬜ |
| Божественная защита (Divine Protection) | 498 | MOD_DAMAGE_PERCENT_TAKEN | Buff | ⬜ (как Shield Wall) |
| Божественный щит (Divine Shield) | 642 | SCHOOL_IMMUNITY | Buff | ⬜ |

## Свет (Holy)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Свет небес (Holy Light) | 635 | Holy | Direct heal | ✅ хил сверен #2 |
| Озарение (Flash of Light) | 19750 | Holy | Direct heal | ✅ хил сверен #2 |
| Изгнание нечисти (Exorcism) | 879 | Holy | Direct | ✅ урон сверен #2 |
| Святой удар (Holy Shock) | 20473 | Holy | Direct (урон/хил) | ⬜ |
| Гнев небес (Holy Wrath) | 2812 | Holy | Direct (AoE) | ✅ урон сверен #2 |
| Воскрешение (Redemption) | 7328 | Holy / рес | Utility | ➖ |
| Божественная благосклонность (Divine Favor) | 20216 | Buff (крит хила) | Buff | ⬜ |

## Защита (Protection)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Праведная ярость (Righteous Fury) | 25780 | MOD_THREAT | Toggle | ➖ |
| Освящение (Consecration) | 26573 | Holy | DoT (земля, AoE) | ⬜ |
| Щит праведника (Avenger's Shield) | 31935 | Holy | Direct (бросок щита) | ✅ урон сверен #2 |
| Щит праведности (Shield of Righteousness) | 61411 | Holy | Direct (требует щит) | ✅ урон сверен #2 |
| Длань спасения (Hand of Reckoning) | 62124 | таунт+урон | Utility | ➖ |
| Священный щит (Holy Shield) | 20925 | MOD_BLOCK_PERCENT | Buff | ⬜ (как Shield Block) |

## Воздаяние (Retribution)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Удар крестоносца (Crusader Strike) | 35395 | Physical (Holy) | Melee | ⬜ |
| Божественная буря (Divine Storm) | 53385 | Physical | Melee (AoE)+heal | ⬜ |
| Молот правосудия (Hammer of Justice) | 853 | Holy / stun | CC | ✅ стан существа + визуал (CrowdControlService) проверено |
| Молот гнева (Hammer of Wrath) | 24275 | Holy | Direct (<20% HP) | ✅ урон сверен #2 |
| Возмездие (Avenging Wrath) | 31884 | MOD_DAMAGE_PERCENT_DONE | Buff | ✅ +20% урона спеллами (проверено); авто-атака/печати — отдельный путь ⬜ |

> **Чинить:** печати/ауры — toggle-механика (есть для аур, печати требуют on-hit); MOD_BLOCK_PERCENT (Holy Shield) и
> MOD_DAMAGE_PERCENT_TAKEN (Divine Protection) — уже умеем (Shield Block/Wall). Иммунитеты/dispel — новые типы.
