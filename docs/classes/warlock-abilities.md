# Чернокнижник — абилки (WoW 3.3.5a)

Ресурс — **мана** + **камни души/осколки**. Школы: Shadow/Fire. Демоны-петы.
Легенда/колонки — [README](README.md).

## Общее

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Дар Бездны (Fel Armor) | 28176 | toggle / щит+spellpower | Toggle | 🟡 эксклюзивная группа (Фаза 2); стат не моделируется |
| Доспех Демона (Demon Armor) | 706 | toggle / броня | Toggle | 🟡 эксклюзивная группа (Фаза 2); стат не моделируется |
| Проклятие Стихий (Curse of the Elements) | 1490 | MOD_RESISTANCE− | Debuff | ⬜ |
| Проклятие Слабости (Curse of Weakness) | 702 | MOD_ATTACK_POWER− | Debuff | ⬜ |
| Проклятие Языков (Curse of Tongues) | 1714 | MOD_CASTING_SPEED− | Debuff | ⬜ |
| Страх (Fear) | 5782 | Fear | CC | ⬜ |
| Порабощение демона (Banish) | 710 | изгнание | CC | ⬜ |
| Камень здоровья/души (Create Healthstone) | 6201 | CreateItem | Utility | 🟡 |
| Призыв демона (Summon Imp/Voidwalker…) | 688… | пет | Summon | ➖ |
| Колодец души (Soulshatter/Drain Soul) | 1120 | Shadow | Channel | ⬜ |

## Колдовство (Affliction)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Порча (Corruption) | 172 | Shadow | DoT | 🟡 |
| Тленное проклятие (Curse of Agony) | 980 | Shadow | DoT | 🟡 |
| Нестабильное проклятие (Unstable Affliction) | 30108 | Shadow | DoT | 🟡 |
| Иссушение жизни (Drain Life) | 689 | Shadow | DoT-канал + heal | ⬜ |
| Чума (Haunt) | 48181 | Shadow | Direct + DoT-мод | ⬜ |
| Посев Порчи (Seed of Corruption) | 27243 | Shadow | DoT + взрыв (AoE) | ⬜ |

## Демонология (Demonology)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Метаморфоза (Metamorphosis) | 59672 | форма демона | Toggle (форма) | ⬜ |
| Удар тенью (Shadow Bite) | 54049 | пет | — | ➖ |
| Сжигание души (Immolation Aura) | 50589 | Fire (AoE) | Buff/DoT | ⬜ |
| Душевный огонь (Soul Fire) | 6353 | Fire | Direct | 🟡 |

## Разрушение (Destruction)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Тень огня (Shadow Bolt) | 686 | Shadow | Direct | 🟡 |
| Сжигание (Immolate) | 348 | Fire | Direct + DoT | 🟡 |
| Поджог (Incinerate) | 29722 | Fire | Direct | 🟡 |
| Хаос (Chaos Bolt) | 50796 | Fire | Direct (игнор. сопр.) | ⬜ |
| Дождь огня (Rain of Fire) | 5740 | Fire | DoT-канал (AoE) | 🟡 |
| Поток теней (Shadowburn) | 17877 | Shadow | Direct | ⬜ |
| Адское пламя (Hellfire) | 1949 | Fire | DoT-канал (на себя) | ⬜ |

> **Чинить:** direct/DoT data-driven (🟡 → проверить); осколки души как ресурс — НЕТ; проклятия — debuff-ауры;
> Fel/Demon Armor — toggle; Metamorphosis — форма; петы/банниш/страх — новые механики.
