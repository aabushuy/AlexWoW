-- M9.3 — тест: классовые тренеры рядом со стартовой точкой человека (Долина Североземья).
-- Цель: проверять список тренера / покупку абилок, не бегая в столицу.
--
-- Спавнит РЕАЛЬНЫХ классовых тренеров из дампа (имя/модель/ассортимент настоящие) в точке старта
-- человека (map 0, ~ -8949.95 -132.49 83.53). Данные-driven: для каждого класса берётся первый реальный
-- классовый тренер (creature_template.TrainerType=0 + TrainerClass=N), у которого есть строки npc_trainer.
-- Зарезервированный диапазон guid 9000001..9000099 — откат одной строкой (см. конец файла).
--
-- Применять ТОЛЬКО на тест-сервере:  mysql ... mangos < tools/scripts/m9.3-test-trainers.sql
-- Это реальные NPC (не помечены [TEST]) — при импорте свежего дампа исчезнут; для ручного удаления — откат.

-- Подчистка предыдущего прогона (идемпотентность).
DELETE FROM creature WHERE guid BETWEEN 9000001 AND 9000099;

-- guid, класс, смещение по Y (тренеры в ряд, тот же Z что у старта).
-- Классы человека: Warrior=1, Paladin=2, Rogue=4, Priest=5, Mage=8, Warlock=9.

INSERT INTO creature (guid, id, map, spawnMask, phaseMask, position_x, position_y, position_z, orientation,
                      spawntimesecsmin, spawntimesecsmax, spawndist, MovementType)
SELECT 9000001, ct.entry, 0, 1, 1, -8949.95, -150.00, 83.53, 0, 120, 120, 0, 0
FROM creature_template ct
WHERE ct.TrainerType = 0 AND ct.TrainerClass = 1
  AND (EXISTS (SELECT 1 FROM npc_trainer nt WHERE nt.entry = ct.entry)
       OR (ct.TrainerTemplateId <> 0
           AND EXISTS (SELECT 1 FROM npc_trainer_template tt WHERE tt.entry = ct.TrainerTemplateId)))
ORDER BY ct.entry LIMIT 1;

INSERT INTO creature (guid, id, map, spawnMask, phaseMask, position_x, position_y, position_z, orientation,
                      spawntimesecsmin, spawntimesecsmax, spawndist, MovementType)
SELECT 9000002, ct.entry, 0, 1, 1, -8949.95, -155.00, 83.53, 0, 120, 120, 0, 0
FROM creature_template ct
WHERE ct.TrainerType = 0 AND ct.TrainerClass = 2
  AND (EXISTS (SELECT 1 FROM npc_trainer nt WHERE nt.entry = ct.entry)
       OR (ct.TrainerTemplateId <> 0
           AND EXISTS (SELECT 1 FROM npc_trainer_template tt WHERE tt.entry = ct.TrainerTemplateId)))
ORDER BY ct.entry LIMIT 1;

INSERT INTO creature (guid, id, map, spawnMask, phaseMask, position_x, position_y, position_z, orientation,
                      spawntimesecsmin, spawntimesecsmax, spawndist, MovementType)
SELECT 9000004, ct.entry, 0, 1, 1, -8949.95, -160.00, 83.53, 0, 120, 120, 0, 0
FROM creature_template ct
WHERE ct.TrainerType = 0 AND ct.TrainerClass = 4
  AND (EXISTS (SELECT 1 FROM npc_trainer nt WHERE nt.entry = ct.entry)
       OR (ct.TrainerTemplateId <> 0
           AND EXISTS (SELECT 1 FROM npc_trainer_template tt WHERE tt.entry = ct.TrainerTemplateId)))
ORDER BY ct.entry LIMIT 1;

INSERT INTO creature (guid, id, map, spawnMask, phaseMask, position_x, position_y, position_z, orientation,
                      spawntimesecsmin, spawntimesecsmax, spawndist, MovementType)
SELECT 9000005, ct.entry, 0, 1, 1, -8949.95, -165.00, 83.53, 0, 120, 120, 0, 0
FROM creature_template ct
WHERE ct.TrainerType = 0 AND ct.TrainerClass = 5
  AND (EXISTS (SELECT 1 FROM npc_trainer nt WHERE nt.entry = ct.entry)
       OR (ct.TrainerTemplateId <> 0
           AND EXISTS (SELECT 1 FROM npc_trainer_template tt WHERE tt.entry = ct.TrainerTemplateId)))
ORDER BY ct.entry LIMIT 1;

INSERT INTO creature (guid, id, map, spawnMask, phaseMask, position_x, position_y, position_z, orientation,
                      spawntimesecsmin, spawntimesecsmax, spawndist, MovementType)
SELECT 9000008, ct.entry, 0, 1, 1, -8949.95, -170.00, 83.53, 0, 120, 120, 0, 0
FROM creature_template ct
WHERE ct.TrainerType = 0 AND ct.TrainerClass = 8
  AND (EXISTS (SELECT 1 FROM npc_trainer nt WHERE nt.entry = ct.entry)
       OR (ct.TrainerTemplateId <> 0
           AND EXISTS (SELECT 1 FROM npc_trainer_template tt WHERE tt.entry = ct.TrainerTemplateId)))
ORDER BY ct.entry LIMIT 1;

INSERT INTO creature (guid, id, map, spawnMask, phaseMask, position_x, position_y, position_z, orientation,
                      spawntimesecsmin, spawntimesecsmax, spawndist, MovementType)
SELECT 9000009, ct.entry, 0, 1, 1, -8949.95, -175.00, 83.53, 0, 120, 120, 0, 0
FROM creature_template ct
WHERE ct.TrainerType = 0 AND ct.TrainerClass = 9
  AND (EXISTS (SELECT 1 FROM npc_trainer nt WHERE nt.entry = ct.entry)
       OR (ct.TrainerTemplateId <> 0
           AND EXISTS (SELECT 1 FROM npc_trainer_template tt WHERE tt.entry = ct.TrainerTemplateId)))
ORDER BY ct.entry LIMIT 1;

-- Что заспавнилось:
-- SELECT c.guid, c.id, ct.Name, ct.TrainerClass FROM creature c
--   JOIN creature_template ct ON ct.entry=c.id WHERE c.guid BETWEEN 9000001 AND 9000099;

-- ОТКАТ:
-- DELETE FROM creature WHERE guid BETWEEN 9000001 AND 9000099;
