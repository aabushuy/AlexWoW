-- Тренировочный манекен 80 ур. в Нортшире (для проверки навыков). #28.
--
-- Клонирует реального Advanced Training Dummy (entry 24792, ур.80, модель манекена 3019, нейтральная
-- фракция 7 — жёлтый, атакуемый) в кастомный entry 990020 с русским именем, спавнит на позиции тест-
-- персонажа. Сервер распознаёт entry 990020 (Npcs.IsTrainingDummy): даёт большой HP (50 млн) и делает
-- его ПАССИВНЫМ (не агрится / не отвечает) — стационарная цель.
--
-- Применять на mangos:  mysql ... mangos < tools/scripts/northshire-training-dummy.sql
-- Идемпотентно; ОТКАТ — в конце. (Кастомный контент — при импорте свежего дампа исчезнет.)

SET NAMES utf8mb4;

-- Позиция в Долине Североземья (map 0). Дев-команда .dummy двигает манекен к ногам игрока в рантайме
-- (in-memory; после рестарта world возвращается на эту точку).
DELETE FROM creature WHERE guid = 9000020;
DELETE FROM creature_template WHERE entry = 990020;

DROP TEMPORARY TABLE IF EXISTS _d;
CREATE TEMPORARY TABLE _d AS SELECT * FROM creature_template WHERE entry = 24792;
UPDATE _d SET entry = 990020, Name = 'Тренировочный манекен', SubName = 'Уровень 80 — проверка навыков';
INSERT INTO creature_template SELECT * FROM _d;
DROP TEMPORARY TABLE _d;

INSERT INTO creature (guid, id, map, spawnMask, phaseMask, position_x, position_y, position_z, orientation,
                      spawntimesecsmin, spawntimesecsmax, spawndist, MovementType)
VALUES (9000020, 990020, 0, 1, 1, -8964.61, -150.95, 81.83, 0, 120, 120, 0, 0);

-- Проверка:
-- SELECT c.guid, ct.entry, ct.Name, ct.MinLevel, ct.Faction, ct.DisplayId1 FROM creature c
--   JOIN creature_template ct ON ct.entry=c.id WHERE c.guid=9000020;

-- ===== ОТКАТ =====
-- DELETE FROM creature WHERE guid = 9000020;
-- DELETE FROM creature_template WHERE entry = 990020;
