# M12 — Spell QA: захват и анализ заклинаний по классам

Систематическая проверка корректности боевых заклинаний каждого класса (урон/хил/DoT/HoT) в базовой
конфигурации (**без талантов** — `SpellMods` пуст, ловим эталонные числа). Сервер уже считает все величины
авторитетно, эталон есть в `spell_template` (дамп Spell.dbc) → захватываем вычисленные значения в БД,
анализируем аномалии на админ-странице Web, заводим **1 тикет в Vikunja на сессию захвата**.

| Срез | Статус |
|---|---|
| **SQA-1** Схема БД (`spell_test_session`/`_result`) + EF-репозиторий | ✅ |
| **SQA-2** Рекордер захвата + дев-команда `.spelltest start/stop/status` (ручной) | ✅ |
| **SQA-3** Лечебный манекен (хил по существу) + `.dummy heal` | ✅ |
| **SQA-4** Авто-харнесс `.spelltest run [N]` | ✅ |
| **SQA-5** Web админ-раздел: список сессий + анализ аномалий | ✅ |
| **SQA-6** Тикет в Vikunja (1 на сессию) + копи-фоллбэк | ✅ |

---

## Ключевые решения

- **Захват — серверный** (источник истины). Аддон не нужен: триггеры — дев-команды (гейт `is_admin`).
- **Эталон сохраняется в строку результата в момент захвата** (`expected_min/max`, школа, стоимость из
  `SpellInfo`), потому что **Web видит только `alexwow_auth`**, без доступа к `mangos`/`spell_template`.
  Анализатор (`SpellTestAnalyzer`) самодостаточен — сверяет вычисленное с эталоном строки.
- **Два режима:** ручной (тестировщик кастует) и авто-харнесс (`.spelltest run` перебирает
  `KnownSpells`, фильтрует, кастует каждый N раз прямым `CompleteCastAsync`, минуя каст-бар/гейты).
- **Хил-манекен (Option A):** хилы в проекте игрок-only (`ApplyHealAsync`→`FindPlayer`). Для проверки
  лечащих спеллов добавлена ветка-существо: лечим HP манекена (faction 35, не атакуем) с клампом ½ макс.;
  призывается раненым на 1% макс. → любой хил даёт `effective>0`. HoT в харнессе — на себя.
- **Запись только при активной сессии** (рекордер keyed by `WorldSession`). DoT/HoT в харнессе —
  синтетический тик (`info.TickAmount`), естественные тики в харнесс-режиме рекордер пропускает (анти-дубль).

## Файлы

- DB: `Entities/SpellTest{Session,Result}.cs`, `Models/SpellTest*.cs`, `Abstractions/ISpellTestRepository.cs`,
  `Repositories/EfSpellTestRepository.cs`, миграция `AddSpellTest`, `Analysis/SpellTestAnalyzer.cs`.
- WorldServer: `World/SpellTestCaptureService.cs`, `Handlers/SpellTestHarnessService.cs`,
  `Handlers/Dev/SpellTestCommand.cs`; хуки в `SpellEffectsService`/`PeriodicsService`; хил-манекен в
  `Protocol/Creatures.cs`+`World/CreatureDirector.cs`; `IsTrainingDummy`→`IsTestDummy`.
- Web: `Pages/Admin/{Index,Session}.*`, `Services/VikunjaTicketService.cs`, claim/policy `Admin`,
  nav-ссылка; стили в `wow.css`.

## Грабли / заметки

- Лечебный манекен: первая итерация ставила HP=½ макс. = потолок лечения → `effective=0`. Исправлено:
  призыв на 1% макс., потолок ½ макс. → запас под лечение на всю сессию.
- Анализатор: нулевой эффект проверяем по **вычисленной** величине (`Amount==0`), а не `effective==0`
  (последнее — норма: цель полна/мертва, овёрхил, не баг спелла). Weapon-абилки исключены из проверки
  диапазона (бросок оружия закономерно выходит за `[min;max]`).
- Vikunja из Web: токен/URL — только в деплое (`Web:Vikunja`, не в репозитории). Если не настроено —
  кнопка скрыта, доступен копи-фоллбэк (готовое тело тикета для ручного заведения / через MCP агентом).

## Проверка

Build 0/0 + тесты 19/19 (вкл. 10 на `SpellTestAnalyzer`). Игровой приём: войти админ-персонажем,
`.learnall` → `.dummy` + `.dummy heal` → `.spelltest run 5` (или ручной `start/stop`) → строки в БД →
Web `/Admin` → анализ → «Завести тикет». Миграцию применяет AuthServer; деплой по `deploy/deploy.ps1`.
