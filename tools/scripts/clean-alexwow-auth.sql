-- ============================================================================
-- Полная очистка БД alexwow_auth (TRUNCATE всех таблиц), КРОМЕ:
--   * realmlist             — список реалмов (конфиг сервера)
--   * dev_teleport          — точки телепорта для дев-команд
--   * __EFMigrationsHistory — журнал EF-миграций (его НЕЛЬЗЯ чистить, иначе мигратор
--                             решит, что схемы нет, и попытается пересоздать таблицы)
--
-- Чистит ВСЁ остальное: account, characters и все character_* / account_data /
-- spell_test_* и т.д. То есть удаляет аккаунты, персонажей и весь их прогресс.
-- TRUNCATE также сбрасывает AUTO_INCREMENT — id начнутся заново с 1.
--
-- Список таблиц вычисляется динамически из information_schema, поэтому новые таблицы
-- (если появятся) тоже будут очищены — без правки этого скрипта.
--
-- ⚠️ ВНИМАНИЕ:
--   * Действие НЕОБРАТИМО. Сделайте дамп, если данные могут понадобиться.
--   * Лучше останавливать auth/world перед запуском (живые сессии держат состояние
--     в памяти и могут перезаписать таблицы при выходе игрока).
--
-- Применять:
--   mysql -u <user> -p alexwow_auth < tools/scripts/clean-alexwow-auth.sql
--   docker exec -i alexwow-mysql mysql -uroot -p<pass> alexwow_auth < tools/scripts/clean-alexwow-auth.sql
-- ============================================================================

USE alexwow_auth;

DROP PROCEDURE IF EXISTS _clean_alexwow_auth;

DELIMITER $$
CREATE PROCEDURE _clean_alexwow_auth()
BEGIN
    DECLARE done INT DEFAULT 0;
    DECLARE tname VARCHAR(64);
    DECLARE cur CURSOR FOR
        SELECT table_name
          FROM information_schema.tables
         WHERE table_schema = 'alexwow_auth'
           AND table_type   = 'BASE TABLE'
           AND table_name NOT IN ('realmlist', 'dev_teleport', '__EFMigrationsHistory');
    DECLARE CONTINUE HANDLER FOR NOT FOUND SET done = 1;

    -- FK-каскадов в схеме нет, но на всякий случай снимаем проверки на время очистки.
    SET FOREIGN_KEY_CHECKS = 0;

    OPEN cur;
    truncate_loop: LOOP
        FETCH cur INTO tname;
        IF done THEN
            LEAVE truncate_loop;
        END IF;
        SET @sql = CONCAT('TRUNCATE TABLE `', tname, '`');
        PREPARE stmt FROM @sql;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END LOOP;
    CLOSE cur;

    SET FOREIGN_KEY_CHECKS = 1;
END$$
DELIMITER ;

CALL _clean_alexwow_auth();
DROP PROCEDURE _clean_alexwow_auth;

-- Контроль: число строк в сохранённых таблицах должно остаться, остальные — пустые.
SELECT 'realmlist'    AS kept_table, COUNT(*) AS rows_left FROM realmlist
UNION ALL
SELECT 'dev_teleport' AS kept_table, COUNT(*) AS rows_left FROM dev_teleport;
