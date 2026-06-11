-- Кастомные ШАБЛОНЫ класс-тренеров с ПОЛНЫМ набором умений (вкл. Рыцаря Смерти). #26.
--
-- ⚠️ ОБНОВЛЕНО (веха Devcommands, D1): тренеры БОЛЬШЕ НЕ СТАВЯТСЯ на карту в Нортшире — их статичные
-- спавны удалены (см. ОТКАТ ниже, выполнен в проде). Эти entry (990001..990011) теперь используются как
-- ШАБЛОНЫ дев-тренеров для команды `.trainer <class>` (in-memory спавн у игрока по требованию;
-- ITrainerRepository.GetClassTrainerEntryAsync выбирает их: TrainerType=0 + прямой npc_trainer +
-- Faction=35). Поэтому ОСТАВЛЯЕМ creature_template + npc_trainer, но НЕ создаём строки creature.
--
-- Зачем такие шаблоны: берём канонический trainer-template класса (городские тренеры) и кладём его спеллы
-- напрямую в наш entry. Фракция 35 (hostileMask=0 → дружелюбны/нейтральны ко ВСЕМ расам), TrainerRace=0
-- (без расового гейта) → доступны любому персонажу нужного класса, включая ДК.
--
-- Канонические trainer-template'ы (creature_template.TrainerTemplateId), проверено по дампу:
--   Воин=11(133)  Паладин=21(175)  Охотник=31(171)  Разбойник=41(124)  Жрец=51(240)
--   Шаман=71(274) Маг=81(256)      Чернокнижник=91(235)  Друид=111(299)
--   Рыцарь Смерти(класс 6) — без шаблона, прямой npc_trainer (entry 28471, 92 спелла).
--
-- Кастомные entry 990001..990011, спавн-guid 9000001..9000011 (в зарезервированном диапазоне M9.3).
-- Идемпотентно: повторный запуск пересоздаёт. ОТКАТ — в конце файла.
-- Применять на mangos:  mysql ... mangos < tools/scripts/northshire-class-trainers.sql
-- (это кастомный контент — при импорте свежего дампа исчезнет; для удаления см. ОТКАТ.)

SET NAMES utf8mb4;

-- Точка старта человека (Долина Североземья, map 0): ~ -8949.95 -132.49 83.53.
-- Кастомные тренеры — рядом, в ряд по Y (лицом к точке старта, orientation ~ pi/2).

-- Подчистка: старые отладочные спавны M9.3 + прошлый прогон этого скрипта.
-- ⚠️ Диапазон 9000001..9000019 — НЕ трогает манекен 9000020 (northshire-training-dummy.sql)!
DELETE FROM creature WHERE guid BETWEEN 9000001 AND 9000019;
DELETE FROM npc_trainer WHERE entry BETWEEN 990001 AND 990011;
DELETE FROM creature_template WHERE entry BETWEEN 990001 AND 990011;

-- Шаблон создания одного тренера: клонируем реального классового тренера (модель/класс/статы),
-- переопределяем фракцию/флаги/имя, очищаем TrainerTemplateId и кладём полный список спеллов напрямую.

-- ===== 1. Воин (template 11) =====
DROP TEMPORARY TABLE IF EXISTS _t;
CREATE TEMPORARY TABLE _t AS SELECT * FROM creature_template WHERE entry=913;
UPDATE _t SET entry=990001, Faction=35, NpcFlags=NpcFlags|0x11, TrainerTemplateId=0,
  MinLevel=80, MaxLevel=80, Name='Наставник воинов', SubName='Полный набор умений';
INSERT INTO creature_template SELECT * FROM _t;
INSERT INTO npc_trainer (entry,spell,spellcost,reqskill,reqskillvalue,reqlevel,ReqAbility1,ReqAbility2,ReqAbility3,condition_id)
SELECT 990001,spell,spellcost,reqskill,reqskillvalue,reqlevel,ReqAbility1,ReqAbility2,ReqAbility3,condition_id
FROM npc_trainer_template WHERE entry=11;

-- ===== 2. Паладин (template 21) =====
DROP TEMPORARY TABLE IF EXISTS _t;
CREATE TEMPORARY TABLE _t AS SELECT * FROM creature_template WHERE entry=16275;
UPDATE _t SET entry=990002, Faction=35, NpcFlags=NpcFlags|0x11, TrainerTemplateId=0,
  MinLevel=80, MaxLevel=80, Name='Наставник паладинов', SubName='Полный набор умений';
INSERT INTO creature_template SELECT * FROM _t;
INSERT INTO npc_trainer (entry,spell,spellcost,reqskill,reqskillvalue,reqlevel,ReqAbility1,ReqAbility2,ReqAbility3,condition_id)
SELECT 990002,spell,spellcost,reqskill,reqskillvalue,reqlevel,ReqAbility1,ReqAbility2,ReqAbility3,condition_id
FROM npc_trainer_template WHERE entry=21;

-- ===== 3. Охотник (template 31) =====
DROP TEMPORARY TABLE IF EXISTS _t;
CREATE TEMPORARY TABLE _t AS SELECT * FROM creature_template WHERE entry=987;
UPDATE _t SET entry=990003, Faction=35, NpcFlags=NpcFlags|0x11, TrainerTemplateId=0,
  MinLevel=80, MaxLevel=80, Name='Наставник охотников', SubName='Полный набор умений';
INSERT INTO creature_template SELECT * FROM _t;
INSERT INTO npc_trainer (entry,spell,spellcost,reqskill,reqskillvalue,reqlevel,ReqAbility1,ReqAbility2,ReqAbility3,condition_id)
SELECT 990003,spell,spellcost,reqskill,reqskillvalue,reqlevel,ReqAbility1,ReqAbility2,ReqAbility3,condition_id
FROM npc_trainer_template WHERE entry=31;

-- ===== 4. Разбойник (template 41) =====
DROP TEMPORARY TABLE IF EXISTS _t;
CREATE TEMPORARY TABLE _t AS SELECT * FROM creature_template WHERE entry=917;
UPDATE _t SET entry=990004, Faction=35, NpcFlags=NpcFlags|0x11, TrainerTemplateId=0,
  MinLevel=80, MaxLevel=80, Name='Наставник разбойников', SubName='Полный набор умений';
INSERT INTO creature_template SELECT * FROM _t;
INSERT INTO npc_trainer (entry,spell,spellcost,reqskill,reqskillvalue,reqlevel,ReqAbility1,ReqAbility2,ReqAbility3,condition_id)
SELECT 990004,spell,spellcost,reqskill,reqskillvalue,reqlevel,ReqAbility1,ReqAbility2,ReqAbility3,condition_id
FROM npc_trainer_template WHERE entry=41;

-- ===== 5. Жрец (template 51) =====
DROP TEMPORARY TABLE IF EXISTS _t;
CREATE TEMPORARY TABLE _t AS SELECT * FROM creature_template WHERE entry=376;
UPDATE _t SET entry=990005, Faction=35, NpcFlags=NpcFlags|0x11, TrainerTemplateId=0,
  MinLevel=80, MaxLevel=80, Name='Наставник жрецов', SubName='Полный набор умений';
INSERT INTO creature_template SELECT * FROM _t;
INSERT INTO npc_trainer (entry,spell,spellcost,reqskill,reqskillvalue,reqlevel,ReqAbility1,ReqAbility2,ReqAbility3,condition_id)
SELECT 990005,spell,spellcost,reqskill,reqskillvalue,reqlevel,ReqAbility1,ReqAbility2,ReqAbility3,condition_id
FROM npc_trainer_template WHERE entry=51;

-- ===== 6. Рыцарь Смерти (прямой npc_trainer, источник entry 28471) =====
DROP TEMPORARY TABLE IF EXISTS _t;
CREATE TEMPORARY TABLE _t AS SELECT * FROM creature_template WHERE entry=28471;
UPDATE _t SET entry=990006, Faction=35, NpcFlags=NpcFlags|0x11, TrainerTemplateId=0,
  MinLevel=80, MaxLevel=80, Name='Наставник рыцарей смерти', SubName='Полный набор умений';
INSERT INTO creature_template SELECT * FROM _t;
INSERT INTO npc_trainer (entry,spell,spellcost,reqskill,reqskillvalue,reqlevel,ReqAbility1,ReqAbility2,ReqAbility3,condition_id)
SELECT 990006,spell,spellcost,reqskill,reqskillvalue,reqlevel,ReqAbility1,ReqAbility2,ReqAbility3,condition_id
FROM npc_trainer WHERE entry=28471;

-- ===== 7. Шаман (template 71) =====
DROP TEMPORARY TABLE IF EXISTS _t;
CREATE TEMPORARY TABLE _t AS SELECT * FROM creature_template WHERE entry=986;
UPDATE _t SET entry=990007, Faction=35, NpcFlags=NpcFlags|0x11, TrainerTemplateId=0,
  MinLevel=80, MaxLevel=80, Name='Наставник шаманов', SubName='Полный набор умений';
INSERT INTO creature_template SELECT * FROM _t;
INSERT INTO npc_trainer (entry,spell,spellcost,reqskill,reqskillvalue,reqlevel,ReqAbility1,ReqAbility2,ReqAbility3,condition_id)
SELECT 990007,spell,spellcost,reqskill,reqskillvalue,reqlevel,ReqAbility1,ReqAbility2,ReqAbility3,condition_id
FROM npc_trainer_template WHERE entry=71;

-- ===== 8. Маг (template 81 — НЕ 71; 27704 ошибочно на шаманском, источник модели 328 Zaldimar) =====
DROP TEMPORARY TABLE IF EXISTS _t;
CREATE TEMPORARY TABLE _t AS SELECT * FROM creature_template WHERE entry=328;
UPDATE _t SET entry=990008, Faction=35, NpcFlags=NpcFlags|0x11, TrainerTemplateId=0,
  MinLevel=80, MaxLevel=80, Name='Наставник магов', SubName='Полный набор умений';
INSERT INTO creature_template SELECT * FROM _t;
INSERT INTO npc_trainer (entry,spell,spellcost,reqskill,reqskillvalue,reqlevel,ReqAbility1,ReqAbility2,ReqAbility3,condition_id)
SELECT 990008,spell,spellcost,reqskill,reqskillvalue,reqlevel,ReqAbility1,ReqAbility2,ReqAbility3,condition_id
FROM npc_trainer_template WHERE entry=81;

-- ===== 9. Чернокнижник (template 91) =====
DROP TEMPORARY TABLE IF EXISTS _t;
CREATE TEMPORARY TABLE _t AS SELECT * FROM creature_template WHERE entry=461;
UPDATE _t SET entry=990009, Faction=35, NpcFlags=NpcFlags|0x11, TrainerTemplateId=0,
  MinLevel=80, MaxLevel=80, Name='Наставник чернокнижников', SubName='Полный набор умений';
INSERT INTO creature_template SELECT * FROM _t;
INSERT INTO npc_trainer (entry,spell,spellcost,reqskill,reqskillvalue,reqlevel,ReqAbility1,ReqAbility2,ReqAbility3,condition_id)
SELECT 990009,spell,spellcost,reqskill,reqskillvalue,reqlevel,ReqAbility1,ReqAbility2,ReqAbility3,condition_id
FROM npc_trainer_template WHERE entry=91;

-- ===== 11. Друид (template 111) =====
DROP TEMPORARY TABLE IF EXISTS _t;
CREATE TEMPORARY TABLE _t AS SELECT * FROM creature_template WHERE entry=3033;
UPDATE _t SET entry=990011, Faction=35, NpcFlags=NpcFlags|0x11, TrainerTemplateId=0,
  MinLevel=80, MaxLevel=80, Name='Наставник друидов', SubName='Полный набор умений';
INSERT INTO creature_template SELECT * FROM _t;
INSERT INTO npc_trainer (entry,spell,spellcost,reqskill,reqskillvalue,reqlevel,ReqAbility1,ReqAbility2,ReqAbility3,condition_id)
SELECT 990011,spell,spellcost,reqskill,reqskillvalue,reqlevel,ReqAbility1,ReqAbility2,ReqAbility3,condition_id
FROM npc_trainer_template WHERE entry=111;

DROP TEMPORARY TABLE IF EXISTS _t;

-- Стойки воина (2457 Боевая / 71 Защитная / 2458 Берсерка) — клонированный шаблон тренера их НЕ содержит
-- (Боевая — стартовая в playercreateinfo_spell, но как тренируемая отсутствовала; Защитная/Берсерка вообще
-- нетренируемы). Добавляем в тренер воина 990001, чтобы .learnall/обучение их выдавали. M12 (QA).
DELETE FROM npc_trainer WHERE entry=990001 AND spell IN (2457,71,2458);
INSERT INTO npc_trainer (entry,spell,spellcost,reqskill,reqskillvalue,reqlevel,ReqAbility1,ReqAbility2,ReqAbility3) VALUES
  (990001,2457,0,0,0,1,0,0,0),   -- Боевая стойка (ур.1)
  (990001,71,  0,0,0,10,0,0,0),  -- Защитная стойка (ур.10)
  (990001,2458,0,0,0,30,0,0,0);  -- Стойка берсерка (ур.30)

-- НЕ спавним тренеров на карте: команда `.trainer <class>` (веха Devcommands) ставит их у игрока
-- по требованию. Прежние статичные спавны (creature guid 9000001..9000011) удалены — см. ОТКАТ.
-- Манекен (guid 9000020, northshire-training-dummy.sql) — отдельная сущность, не трогаем.

-- Проверка:
-- SELECT c.guid, ct.entry, ct.Name, ct.TrainerClass,
--   (SELECT COUNT(*) FROM npc_trainer nt WHERE nt.entry=ct.entry) spells
-- FROM creature c JOIN creature_template ct ON ct.entry=c.id
-- WHERE c.guid BETWEEN 9000001 AND 9000099 ORDER BY ct.TrainerClass;

-- ===== СНЯТИЕ СПАВНОВ С КАРТЫ (выполнено в проде, веха Devcommands D1) =====
-- ⚠️ Диапазон 9000001..9000019 — НЕ трогает манекен 9000020!
-- DELETE FROM creature WHERE guid BETWEEN 9000001 AND 9000019;
--
-- ===== ПОЛНЫЙ ОТКАТ (если нужно убрать и шаблоны — сломает .trainer) =====
-- DELETE FROM creature WHERE guid BETWEEN 9000001 AND 9000019;
-- DELETE FROM npc_trainer WHERE entry BETWEEN 990001 AND 990011;
-- DELETE FROM creature_template WHERE entry BETWEEN 990001 AND 990011;
