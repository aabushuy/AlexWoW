# Шаман — абилки (WoW 3.3.5a)

Ресурс — **мана**. Тотемы (4 стихии). Школы: Nature/Fire/Frost. Оружейные чары.
Легенда/колонки — [README](README.md).

## Общее

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Призрачный волк (Ghost Wolf) | 2645 | форма 16 / MOD_SPEED | Toggle (форма) | ⬜ |
| Очищение духа (Cleanse Spirit) | 51886 | dispel | Utility | ⬜ |
| Цепи земли (Earthbind Totem) | 2484 | тотем / замедл. | Summon (тотем) | ⬜ |
| Воскрешение (Ancestral Spirit) | 2008 | рес | Utility | ➖ |
| Камнекожий тотем (Stoneskin Totem) | 8071 | тотем-аура | Summon | ⬜ |
| Тотем ярости ветра (Windfury Totem) | 8512 | тотем-аура / on-hit | Summon | ⬜ |
| Оружие Камнекожи / Ярости ветра (Weapon Imbue) | 8232 | on-hit чары оружия | Buff(оружие) | ⬜ |

## Стихии (Elemental)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Удар молнии (Lightning Bolt) | 403 | Nature | Direct | 🟡 |
| Цепная молния (Chain Lightning) | 421 | Nature | Direct (прыжки) | ⬜ |
| Шок землетрясения (Earth Shock) | 8042 | Nature | Direct (мгн.) | 🟡 |
| Шок пламени (Flame Shock) | 8050 | Fire | Direct + DoT | 🟡 |
| Шок мороза (Frost Shock) | 8056 | Frost | Direct + замедл. | 🟡 |
| Тотем огненной стихии (Fire Elemental Totem) | 2894 | призыв | Summon | ➖ |
| Стихийная мощь (Elemental Mastery) | 16166 | MOD_CASTING_SPEED | Buff | ⬜ |

## Совершенствование (Enhancement)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Удар бури (Stormstrike) | 17364 | Physical (Nature) | Melee | ⬜ |
| Удар лавы (Lava Lash) | 60103 | Fire | Melee | ⬜ |
| Волчья стая (Feral Spirit) | 51533 | призыв волков | Summon | ➖ |
| Шаманская ярость (Shamanistic Rage) | 30823 | MOD_DAMAGE_PERCENT_TAKEN | Buff | ⬜ (как Shield Wall) |

## Исцеление (Restoration)

| Абилка | Спелл | Школа/Аура | Тип | Статус |
|---|---|---|---|---|
| Волна исцеления (Healing Wave) | 331 | Nature | Direct heal | 🟡 |
| Малая волна исцеления (Lesser Healing Wave) | 8004 | Nature | Direct heal | 🟡 |
| Цепь исцеления (Chain Heal) | 1064 | Nature | Heal (прыжки) | ⬜ |
| Поток исцеления (Riptide) | 61295 | Nature | Direct heal + HoT | 🟡 |
| Тотем целебного потока (Healing Stream Totem) | 5394 | тотем-HoT | Summon | ⬜ |
| Земной щит (Earth Shield) | 974 | charges-heal | Buff | ⬜ |

> **Чинить:** урон/хил/DoT data-driven (🟡 → проверить); ТОТЕМЫ — отдельная механика (сущность-аура на земле, как ноды);
> Ghost Wolf — форма (как стойка); оружейные чары — on-hit; Shamanistic Rage — MOD_DAMAGE_PERCENT_TAKEN (умеем).
