# Жрец — абилки (WoW 3.3.5a)

Ресурс — **мана**. Школы: Holy (свет/хил), Shadow (тьма). Discipline — щиты/баффы.
Легенда/колонки — [README](README.md).

## Общее

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Слово Силы: Стойкость (Power Word: Fortitude) | 1243 | MOD_STAT (вынос.) | Buff | 🟡 |
| Божественный дух (Divine Spirit) | 14752 | MOD_STAT (дух) | Buff | ⬜ |
| Воскрешение (Resurrection) | 2006 | рес | Utility | ➖ |
| Снятие магии (Dispel Magic) | 527 | dispel | Utility | ⬜ |
| Снятие болезни (Cure Disease) | 528 | dispel | Utility | ⬜ |
| Левитация (Levitate) | 1706 | MOD_FLY/feather | Buff | ⬜ |
| Психический крик (Psychic Scream) | 8122 | Fear (AoE) | CC | ⬜ |
| Уход в тень (Shadowmeld? нет) / Fade | 586 | MOD_THREAT− | Utility | ➖ |

## Послушание (Discipline)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Слово Силы: Щит (Power Word: Shield) | 17 | SCHOOL_ABSORB | Buff (щит) | ⬜ |
| Усиление (Power Infusion) | 10060 | MOD_CASTING_SPEED | Buff | ⬜ |
| Болевое подавление (Pain Suppression) | 33206 | MOD_DAMAGE_PERCENT_TAKEN | Buff | ⬜ (как Shield Wall) |
| Покаяние (Penance) | 47540 | Holy | Channel (урон/хил) | ⬜ |
| Слово Силы: Барьер (Barrier) | 62618 | absorb (земля) | Buff | ⬜ |

## Свет (Holy)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Малое исцеление (Lesser Heal) | 2050 | Holy | Direct heal | 🟡 |
| Исцеление (Heal/Greater Heal) | 2060 | Holy | Direct heal | 🟡 |
| Быстрое исцеление (Flash Heal) | 2061 | Holy | Direct heal | 🟡 |
| Обновление (Renew) | 139 | Holy | HoT | 🟡 |
| Молитва исцеления (Prayer of Healing) | 596 | Holy | Direct heal (группа) | ⬜ |
| Слово Силы: Спасение (Guardian Spirit) | 47788 | спас-кулдаун | Buff | ⬜ |
| Священное слово: Спокойствие/Кара | 88684/88625 | Holy | Direct | ⬜ |

## Тьма (Shadow)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Слово Тьмы: Боль (Shadow Word: Pain) | 589 | Shadow | DoT | 🟡 |
| Разум: Бичевание (Mind Blast) | 8092 | Shadow | Direct | 🟡 |
| Кара разума (Mind Flay) | 15407 | Shadow | DoT-канал + замедл. | 🟡 |
| Слово Тьмы: Смерть (Shadow Word: Death) | 32379 | Shadow | Direct (откат на себя) | ⬜ |
| Вампирическое прикосновение (Vampiric Touch) | 34914 | Shadow | DoT | 🟡 |
| Чума разума (Devouring Plague) | 2944 | Shadow | DoT | 🟡 |
| Облик Тьмы (Shadowform) | 15473 | форма / MOD_DAMAGE% | Toggle (форма) | ⬜ |
| Дисперсия (Dispersion) | 47585 | MOD_DAMAGE_PERCENT_TAKEN | Buff | ⬜ |

> **Чинить:** хил/DoT уже data-driven (🟡 → проверить). Новое: абсорб-щиты (PW:Shield), форма Shadowform (как стойка),
> Fear/dispel, MOD_DAMAGE_PERCENT_TAKEN (Pain Suppression/Dispersion — умеем по Shield Wall).
