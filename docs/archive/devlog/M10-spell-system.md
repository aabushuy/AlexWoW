# M10 — спелл-система на данных

Журнал граблей вехи M10. Срезы M10.1–M10.5 делались до заведения этого файла — их история в
коммитах и [roadmap](../roadmap.md); ниже — начиная с M10.6.

## M10.6 — SpellModifier: таланты влияют на абилки (задача #78)

**Проблема.** Таланты выбирались (M9.6–9.8), но пассивные «Улучшение …» не влияли на абилки:
Improved Heroic Strike не снижал ярость, Improved Rend не растил тик. В WotLK такие таланты —
ауры-модификаторы спеллов (`SPELL_AURA_ADD_FLAT_MODIFIER`=107 / `SPELL_AURA_ADD_PCT_MODIFIER`=108),
а подсистемы SpellModifier не было.

**Решение** (эталон — CMaNGOS `Aura::HandleAddModifier` / `Player::ApplySpellMod` /
`SpellEntry::IsFitToFamilyMask`):

- `spell_template` дочитан: `SpellFamilyName`, `SpellFamilyFlags`(+2), `EffectSpellClassMask*_*`
  (96-битные маски), `EffectMiscValue*` = тип SPELLMOD_*.
- Чистая математика — `World/SpellModifiers.cs` (extract/match/apply, покрыта xUnit-тестами в новом
  `tests/AlexWoW.WorldServer.Tests`); формула CMaNGOS: `(база + Σфлэт) × Πпроцентов`.
- Реестр на сессии (`SessionProgressionState.SpellMods`) собирает `SpellModifierService`:
  при входе — пересборка по KnownSpells (батч), при изучении — инкрементально из уже полученного
  шаблона, при сбросе талантов — снятие по источнику.
- Точки применения: стоимость (`SpellCastService.EffectivePowerCost`, SPELLMOD_COST), урон/хил
  (`SpellEffectsService`, ALL_EFFECTS/EFFECT{N} к величине эффекта + SPELLMOD_DAMAGE к итогу),
  тик DoT/HoT и длительность (`PeriodicsService`, SPELLMOD_DOT/DURATION), каст-тайм/КД
  (`SpellCastService.HandleCastAsync`, копией `SpellInfo` через `with` — кэш каталога не трогаем).

### Грабли

1. **Ранг-спеллы талантов НЕ записаны в `spell_chain`** (там только тренируемые цепочки):
   дедуп «активен только высший ранг» через `prev_spell` для пассивок не работает — ранги бы
   суммировались (−1 −2 = −3 ярости вместо −2). CMaNGOS строит цепочки рангов талантов из
   `TalentEntry` программно — сделали так же: при пересборке ранги одного таланта дедупятся по
   `talent.Rank1..5`, при изучении следующего ранга моды прежнего снимает `TalentHandlers`.
2. **Improved Rend — это не SPELLMOD_DAMAGE и не SPELLMOD_DOT**, а `SPELLMOD_EFFECT1` (MiscValue=3):
   модифицируется величина эффекта №1 Кровопускания (периодическая аура). А Improved Cleave —
   `SPELLMOD_ALL_EFFECTS` (8), применяется к бонусу эффекта, НЕ к броску оружия (сверено с
   `Unit::CalculateSpellEffectValue`). Поэтому в `SpellInfo` добавлены индексы прямого/периодического
   эффекта (1..3) — для адресации EFFECT{N}.
3. Величина модификатора = `BasePoints + 1` (12282: −11 → −10), и для ярости она уже ×10 —
   как и стоимость в `spell_template` (150 = 15 ярости), отдельной нормализации не нужно.

### Проверка

Юнит-тесты на реальных строках дампа (12282/12286/12329 против 78/772/845); смоук-запуск сервера
(84 опкода, БД мира подключена); живым клиентом: воин + Improved Heroic Strike → стоимость 14/13/12
ярости, Improved Rend → тик растёт.

### Грабли второго захода (по тесту клиентом 2026-06-11)

Клиент подтвердил: Improved Rend работает, но Improved Heroic Strike «не снижает стоимость», а
Рывок не даёт ярость. Обе причины — не в матчинге:

4. **Клиент считает стоимость сам из своей DBC** — тултип/гейт кнопки не знают о серверных
   модификаторах, пока им не пришлют **`SMSG_SET_FLAT/PCT_SPELL_MODIFIER` (0x266/0x267)**:
   u8 eff (бит 0–95 classmask) + u8 op + i32 итог по биту (CMaNGOS `Player::AddSpellMod`).
   Сервер списывал 14 ярости, но клиент показывал 15 и не давал нажать кнопку при 14.
   Добавлен `SyncClientAsync` в `SpellModifierService`: дифф итогов по (бит, op, flat/pct)
   против последней отправки (`SentSpellModTotals`), исчезнувшие биты зануляются (сброс талантов).
5. **Ярость Рывка закодирована DUMMY-эффектом** (Effect2=3, BasePoints 89/119/149 → 9/12/15 ярости
   ×10), а не ENERGIZE — ядра скриптуют её (TrinityCore `spell_warr_charge`). Реализован generic
   `SPELL_EFFECT_ENERGIZE` (30) + спец-случай «воин + эффект CHARGE + DUMMY с bp>0 → ярость»;
   начисление в `SpellCastCompletion` через `CombatResourcesService.GainPowerAsync` (кап).
   Величина идёт через `ApplyEffectValue` → талант «Улучшенный рывок» (107, ALL_EFFECTS,
   +50/+100, маска 1) применяется автоматически: 14/19 ярости.

### Отложено

SPELLMOD_CRITICAL_CHANCE (нет модели спелл-крита), SPELLMOD_GLOBAL_COOLDOWN (клиент предсказывает
GCD сам), THREAT/RANGE/CHARGES и прочие редкие SpellModOp; charges-модификаторы (расходуемые,
напр. прок «следующий каст дешевле»).
