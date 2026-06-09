CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `ProductVersion` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)
) CHARACTER SET=utf8mb4;

START TRANSACTION;
DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260609075753_InitialCreate') THEN

    ALTER DATABASE CHARACTER SET utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260609075753_InitialCreate') THEN

    CREATE TABLE `account` (
        `id` int unsigned NOT NULL AUTO_INCREMENT,
        `username` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
        `salt` binary(32) NOT NULL,
        `verifier` binary(32) NOT NULL,
        `session_key` binary(40) NULL,
        `last_ip` varchar(45) CHARACTER SET utf8mb4 NULL,
        `created_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
        `is_admin` tinyint unsigned NOT NULL DEFAULT 0,
        CONSTRAINT `PK_account` PRIMARY KEY (`id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260609075753_InitialCreate') THEN

    CREATE TABLE `account_data` (
        `owner_id` int unsigned NOT NULL,
        `is_char` tinyint unsigned NOT NULL,
        `data_type` tinyint unsigned NOT NULL,
        `update_time` int unsigned NOT NULL DEFAULT 0,
        `data` longblob NULL,
        CONSTRAINT `PK_account_data` PRIMARY KEY (`owner_id`, `is_char`, `data_type`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260609075753_InitialCreate') THEN

    CREATE TABLE `character_action` (
        `owner_guid` int unsigned NOT NULL,
        `button` tinyint unsigned NOT NULL,
        `packed_data` int unsigned NOT NULL,
        CONSTRAINT `PK_character_action` PRIMARY KEY (`owner_guid`, `button`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260609075753_InitialCreate') THEN

    CREATE TABLE `character_aura` (
        `owner_guid` int unsigned NOT NULL,
        `spell` int unsigned NOT NULL,
        `form` tinyint unsigned NOT NULL DEFAULT 0,
        CONSTRAINT `PK_character_aura` PRIMARY KEY (`owner_guid`, `spell`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260609075753_InitialCreate') THEN

    CREATE TABLE `character_declined_names` (
        `owner_guid` int unsigned NOT NULL,
        `n0` varchar(24) CHARACTER SET utf8mb4 NOT NULL DEFAULT '',
        `n1` varchar(24) CHARACTER SET utf8mb4 NOT NULL DEFAULT '',
        `n2` varchar(24) CHARACTER SET utf8mb4 NOT NULL DEFAULT '',
        `n3` varchar(24) CHARACTER SET utf8mb4 NOT NULL DEFAULT '',
        `n4` varchar(24) CHARACTER SET utf8mb4 NOT NULL DEFAULT '',
        CONSTRAINT `PK_character_declined_names` PRIMARY KEY (`owner_guid`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260609075753_InitialCreate') THEN

    CREATE TABLE `character_items` (
        `item_guid` int unsigned NOT NULL AUTO_INCREMENT,
        `owner_guid` int unsigned NOT NULL,
        `item_entry` int unsigned NOT NULL,
        `bag` tinyint unsigned NOT NULL DEFAULT 255,
        `slot` tinyint unsigned NOT NULL,
        `stack_count` int unsigned NOT NULL DEFAULT 1,
        CONSTRAINT `PK_character_items` PRIMARY KEY (`item_guid`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260609075753_InitialCreate') THEN

    CREATE TABLE `character_queststatus` (
        `owner_guid` int unsigned NOT NULL,
        `quest_id` int unsigned NOT NULL,
        `slot` tinyint unsigned NOT NULL DEFAULT 0,
        `status` tinyint unsigned NOT NULL DEFAULT 0,
        `counter0` smallint unsigned NOT NULL DEFAULT 0,
        `counter1` smallint unsigned NOT NULL DEFAULT 0,
        `counter2` smallint unsigned NOT NULL DEFAULT 0,
        `counter3` smallint unsigned NOT NULL DEFAULT 0,
        CONSTRAINT `PK_character_queststatus` PRIMARY KEY (`owner_guid`, `quest_id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260609075753_InitialCreate') THEN

    CREATE TABLE `character_spell` (
        `owner_guid` int unsigned NOT NULL,
        `spell` int unsigned NOT NULL,
        CONSTRAINT `PK_character_spell` PRIMARY KEY (`owner_guid`, `spell`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260609075753_InitialCreate') THEN

    CREATE TABLE `characters` (
        `guid` int unsigned NOT NULL AUTO_INCREMENT,
        `account_id` int unsigned NOT NULL,
        `name` varchar(12) CHARACTER SET utf8mb4 NOT NULL,
        `race` tinyint unsigned NOT NULL,
        `class` tinyint unsigned NOT NULL,
        `gender` tinyint unsigned NOT NULL,
        `skin` tinyint unsigned NOT NULL,
        `face` tinyint unsigned NOT NULL,
        `hair_style` tinyint unsigned NOT NULL,
        `hair_color` tinyint unsigned NOT NULL,
        `facial_hair` tinyint unsigned NOT NULL,
        `level` tinyint unsigned NOT NULL DEFAULT 1,
        `zone` int unsigned NOT NULL DEFAULT 0,
        `map` int unsigned NOT NULL DEFAULT 0,
        `position_x` float NOT NULL DEFAULT 0,
        `position_y` float NOT NULL DEFAULT 0,
        `position_z` float NOT NULL DEFAULT 0,
        `created_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
        `money` int unsigned NOT NULL DEFAULT 1000000,
        `xp` int unsigned NOT NULL DEFAULT 0,
        `action_bars` tinyint unsigned NOT NULL DEFAULT 0,
        CONSTRAINT `PK_characters` PRIMARY KEY (`guid`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260609075753_InitialCreate') THEN

    CREATE TABLE `realmlist` (
        `id` int unsigned NOT NULL AUTO_INCREMENT,
        `name` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
        `address` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
        `port` smallint unsigned NOT NULL DEFAULT 8085,
        `type` tinyint unsigned NOT NULL DEFAULT 0,
        `flags` tinyint unsigned NOT NULL DEFAULT 0,
        `timezone` tinyint unsigned NOT NULL DEFAULT 1,
        `population` float NOT NULL DEFAULT 0,
        CONSTRAINT `PK_realmlist` PRIMARY KEY (`id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260609075753_InitialCreate') THEN

    CREATE UNIQUE INDEX `uk_account_username` ON `account` (`username`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260609075753_InitialCreate') THEN

    CREATE INDEX `ix_aura_owner` ON `character_aura` (`owner_guid`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260609075753_InitialCreate') THEN

    CREATE INDEX `ix_items_owner` ON `character_items` (`owner_guid`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260609075753_InitialCreate') THEN

    CREATE INDEX `ix_qs_owner` ON `character_queststatus` (`owner_guid`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260609075753_InitialCreate') THEN

    CREATE INDEX `ix_spell_owner` ON `character_spell` (`owner_guid`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260609075753_InitialCreate') THEN

    CREATE INDEX `ix_characters_account` ON `characters` (`account_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260609075753_InitialCreate') THEN

    CREATE UNIQUE INDEX `uk_characters_name` ON `characters` (`name`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260609075753_InitialCreate') THEN

    CREATE UNIQUE INDEX `uk_realmlist_name` ON `realmlist` (`name`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260609075753_InitialCreate') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260609075753_InitialCreate', '9.0.0');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

