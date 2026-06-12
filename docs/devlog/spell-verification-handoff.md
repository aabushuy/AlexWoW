# Хэндофф: проверка абилок/талантов классов (промт для новой сессии)

Скопируй блок ниже в начало новой сессии — он самодостаточный.

---

```
Контекст: проект AlexWoW — самописный сервер WoW 3.3.5a (WotLK, build 12340) на .NET 10 + MySQL.
Рабочая папка C:\repo\AlexWoW. Эталоны для сверки механик/формул: C:\repo\mangos-wotlk (CMaNGOS,
src/game/Spells/, SpellAuraDefines.h) и C:\repo\TrinityCore. Клиент: C:\Wow335. Веб-панель: alexwow.home.srv.

Задача: постепенно проверять и чинить АБИЛКИ и ТАЛАНТЫ классов (и расовые). Трекинг — в docs/:
- docs/classes/mechanics.md — ГЛАВНЫЙ файл: кросс-индекс по механикам + порядок работы (читай первым).
- docs/classes/<class>-abilities.md и <class>-talents.md — абилки/таланты по классам (статусы ✅/🟡/⬜/➖,
  колонки: спелл-id, школа/тип ауры, тип эффекта).
- docs/races/<race>.md — расовые абилки.
По мере проверки обновляй статусы в этих файлах.

Стратегия (из mechanics.md): движок спеллов data-driven из spell_template (M10.2), поэтому чиним и
проверяем ПО МЕХАНИКЕ, не по школе/классу — одна правка зажигает абилку у всех классов.
- Фаза 1 (сейчас): массовая сверка чисел. В игре админ-персонажем: .dummy (и .dummy heal), затем
  .spelltest run — харнес M12 авто-кастует известные спеллы класса и сверяет урон/хил/DoT с эталоном
  из spell_template; аномалии видны на https://alexwow.home.srv → раздел Spell QA. Цель — перевести
  🟡→✅ или найти пробел в SpellCatalog.FromTemplate. Начать с Воина и Мага.
- Фаза 2: механики по убыванию отдачи — формы/toggle → ресурсы (combo points/руны) → митигейшн/absorb
  → CC → interrupt/dispel → procs → петы (см. mechanics.md, там списки абилок по каждой механике).

Ключевые файлы кода (src/AlexWoW.WorldServer):
- World/SpellCatalog.cs — парсинг spell_template → SpellInfo (типы эффектов/аур; сюда добавлять
  распознавание новых аур, как делали MOD_BLOCK_PERCENT 51, MOD_DAMAGE_PERCENT_TAKEN 87).
- Handlers/SpellCastService.cs, SpellCastCompletion.cs, SpellEffectsService.cs — каст и прямые эффекты.
- Handlers/PeriodicsService.cs — DoT/HoT и непериодические баффы со стат-эффектом (HP/блок/урон-получаемый).
- Handlers/AuraService.cs — ауры/формы/стойки (эксклюзивные группы toggle).
- World/CombatStats.cs, Handlers/CreatureCombatAI.cs — защитные статы и обработка входящего удара.
- Handlers/SpellTestHarnessService.cs, World/SpellTestCaptureService.cs — харнес M12 Spell QA.

Дев-инструменты в игре (аддон AlexDevCmd, кнопка у миникарты / /dev): .setlevel, .learn/.learnall,
.dummy [heal|attack], .spelltest start/stop/run, .trainer <class>, .buff/.unbuff и др.

Рабочий процесс (ВАЖНО, зафиксировано в памяти):
- После каждой завершённой задачи: ВСЕГДА git commit + деплой (deploy/deploy.ps1, публикует локально +
  docker compose up на homeserver). ПУШ — только по явной просьбе пользователя.
- Деплой перезапускает auth/world (игрока выкидывает — нужен релог). docs-only изменения деплоить не нужно.
- Сборка/тесты: dotnet build C:\repo\AlexWoW\AlexWoW.slnx -c Release; dotnet test ...\AlexWoW.slnx.
  Есть тесты в tests/AlexWoW.WorldServer.Tests (CombatStats/SpellCatalog golden-дайджест — при изменении
  парсера SpellInfo обновлять ExpectedDigest осознанно).
- Vikunja-трекер: https://tasks.home.srv, проект 11 = M8 Web (токен в памяти проекта). Vikunja-задачи
  по спеллам заводить по запросу.
- Пользователь проверяет вживую и присылает скриншоты; работаем итеративно: он называет конкретный баг —
  я чиню точечно с тестом, коммичу, деплою.

Начни с того, что прочитай docs/classes/mechanics.md и предложи конкретный первый шаг Фазы 1
(прогон .spelltest по Воину/Магу) или жди, пока пользователь назовёт абилку/механику.
```

---

> Обновлять этот файл, если меняется процесс/инструменты. Связано: [classes/mechanics.md](../classes/mechanics.md),
> [M10-spell-system.md](M10-spell-system.md), [M12-spell-qa.md](M12-spell-qa.md).
