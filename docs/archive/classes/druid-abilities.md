# Друид — абилки (WoW 3.3.5a)

Ресурс — **мана** (формы: ярость медведя / энергия кошки). Формы — toggle (как стойки). Школы: Nature/Arcane.
Легенда/колонки — [README](README.md).

## Общее

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Облик медведя (Bear/Dire Bear Form) | 5487 | форма 5/8 | Toggle (форма) | 🟡 (система форм) |
| Облик кошки (Cat Form) | 768 | форма 1 | Toggle (форма) | 🟡 |
| Облик путешествия (Travel Form) | 783 | форма 3 / MOD_SPEED | Toggle (форма) | ⬜ |
| Облик луны (Moonkin Form) | 24858 | форма / MOD_DAMAGE% | Toggle (форма) | ⬜ |
| Древо жизни (Tree of Life) | 33891 | форма / MOD_HEALING | Toggle (форма) | ⬜ |
| Метка Дикой природы (Mark of the Wild) | 1126 | MOD_TOTAL_STAT% | Buff | 🟡 |
| Возрождение (Rebirth) | 20484 | бой-рес | Utility | ➖ |
| Искоренение проклятия/яда (Remove Curse/Abolish Poison) | 2782 | dispel | Utility | ⬜ |
| Спячка (Hibernate) | 2637 | CC (звери) | CC | ➖ |

## Баланс (Balance)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Гневное светило (Wrath) | 5176 | Nature | Direct | 🟡 |
| Звёздный огонь (Starfire) | 2912 | Arcane | Direct | 🟡 |
| Лунный огонь (Moonfire) | 8921 | Arcane | Direct + DoT | 🟡 |
| Солнечный огонь (Insect Swarm) | 5570 | Nature | DoT | 🟡 |
| Звездопад (Starfall) | 48505 | Arcane | DoT-канал (AoE) | ⬜ |
| Ураган (Hurricane) | 16914 | Nature | DoT-канал (AoE) | 🟡 |

## Сила зверя (Feral Combat)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Коготь (Claw) | 1082 | Physical | Melee+CP (кошка) | ⬜ |
| Разорвать (Rip) | 1079 | Physical | DoT (расход CP) | 🟡 |
| Кровожадность (Ferocious Bite) | 22568 | Physical | Melee (расход CP) | ⬜ |
| Потрошение (Mangle) | 33878 | Physical | Melee (медведь/кошка) | ⬜ |
| Свирепый рык (Swipe) | 779 | Physical (AoE) | Melee | ⬜ |
| Раздражение (Maul) | 6807 | Physical | Melee (медведь, ярость) | ⬜ |
| Барскин (Barkskin) | 22812 | MOD_DAMAGE_PERCENT_TAKEN | Buff | ⬜ (как Shield Wall) |

## Исцеление (Restoration)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Оживление (Rejuvenation) | 774 | Nature | HoT | 🟡 |
| Залечивание ран (Healing Touch) | 5185 | Nature | Direct heal | 🟡 |
| Покровы (Regrowth) | 8936 | Nature | Direct heal + HoT | 🟡 |
| Быстрый рост (Wild Growth) | 48438 | Nature | HoT (группа) | ⬜ |
| Цветение жизни (Lifebloom) | 33763 | Nature | HoT + взрыв-хил | ⬜ |
| Безмятежность (Tranquility) | 740 | Nature | HoT-канал (группа) | ⬜ |

> **Чинить:** урон/хил/DoT/HoT data-driven (🟡 → проверить); ФОРМЫ — toggle (есть система стоек/форм, нужны формы друида
> 1/3/5/8 + ресурс ярость/энергия в форме); combo points (кошка) — НЕТ; Barkskin/Moonkin — MOD_*-ауры (умеем).
