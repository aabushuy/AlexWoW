# Рыцарь смерти — абилки (WoW 3.3.5a)

Ресурс — **руны** (кровь/мороз/нечестие) + **сила рун (runic power)**. Школы: Physical/Frost/Shadow. Присутствия — toggle.
Легенда/колонки — [README](README.md).

## Общее

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Присутствие крови (Blood Presence) | 48266 | toggle / форма | Toggle | ⬜ |
| Присутствие мороза (Frost Presence) | 48263 | toggle | Toggle | ⬜ |
| Присутствие нечестивости (Unholy Presence) | 48265 | toggle | Toggle | ⬜ |
| Ледяная хватка (Death Grip) | 49576 | таунт+рывок цели | Utility | ⬜ |
| Удар по сухожилиям (Chains of Ice) | 45524 | Frost / замедление | Debuff | ⬜ |
| Удушающий захват (Strangulate) | 47476 | silence | CC | ⬜ |
| Власть над разумом (Mind Freeze) | 47528 | interrupt | Interrupt | ⬜ |
| Сопротивление магии (Anti-Magic Shell) | 48707 | SCHOOL_ABSORB (магия) | Buff | ⬜ |
| Восстание (Raise Dead) | 46584 | гуль-пет | Summon | ➖ |
| Ледяные оковы (Icebound Fortitude) | 48792 | MOD_DAMAGE_PERCENT_TAKEN | Buff | ⬜ (как Shield Wall) |

## Кровь (Blood)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Удар по крови (Blood Strike) | 45902 | Physical | Melee (руны крови) | ⬜ |
| Удар сердца (Heart Strike) | 55050 | Physical | Melee | ⬜ |
| Кровавая язва (Blood Boil) | 48721 | Shadow | Direct (AoE) | ⬜ |
| Высасывание (Death Strike) | 49998 | Physical + heal | Melee | ⬜ |
| Кровавая жажда (Rune Tap) | 48982 | heal | Utility | ⬜ |

## Мороз (Frost)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Ледяное прикосновение (Icy Touch) | 45477 | Frost | Direct + Frost Fever (DoT) | 🟡 |
| Удар чумы (Plague Strike) | 45462 | Physical | Direct + Blood Plague (DoT) | 🟡 |
| Удар обморожения (Obliterate) | 49020 | Physical | Melee | ⬜ |
| Завывание ветра (Howling Blast) | 49184 | Frost | Direct (AoE) | ⬜ |
| Столп мороза (Frost Strike) | 49143 | Frost | Melee (runic power) | ⬜ |
| Колонна льда (Pillar of Frost) | 51271 | Buff | Buff | ⬜ |

## Нечестивость (Unholy)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Удар скверны (Scourge Strike) | 55090 | Physical+Shadow | Melee | ⬜ |
| Чума смерти (Death and Decay) | 43265 | Shadow | DoT (земля, AoE) | ⬜ |
| Извержение чумы (Pestilence) | 50842 | разнос болезней | Utility | ⬜ |
| Тёмное превращение (Dark Transformation) | 63560 | пет-бафф | Buff | ➖ |
| Армия мёртвых (Army of the Dead) | 42650 | призыв | Summon | ➖ |

> **Чинить:** руны/runic power как ресурс — НЕТ (нужно завести); болезни (Frost Fever/Blood Plague) — DoT (умеем);
> присутствия — toggle-формы; Anti-Magic Shell — абсорб; Icebound Fortitude — MOD_DAMAGE_PERCENT_TAKEN (умеем).
