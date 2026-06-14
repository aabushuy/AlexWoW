-- БД трекинга прогресса AlexWoW (источник истины для дашборда; перенос из docs/*.md).
-- Применять однократно вместе с project-seed.sql:
--   cat deploy/sql/project-schema.sql deploy/sql/project-seed.sql | ssh homeserver "docker exec -i alexwow-mysql mysql -ualexwow -palexwow"
-- Схема идемпотентна (IF NOT EXISTS); сид делает TRUNCATE+INSERT (повторный прогон = ре-миграция из md).

CREATE DATABASE IF NOT EXISTS project CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

-- Кросс-классовые механики (docs/classes/mechanics.md §2).
CREATE TABLE IF NOT EXISTS project.Mechanics (
    id       INT AUTO_INCREMENT PRIMARY KEY,
    phase    VARCHAR(32)   NOT NULL DEFAULT '',  -- «Фаза 1» / «Фаза 2»
    section  VARCHAR(160)  NOT NULL DEFAULT '',  -- «3. Митигейшн …»
    item     VARCHAR(255)  NOT NULL DEFAULT '',  -- 1-я колонка (аура/что)
    classes  VARCHAR(255)  NOT NULL DEFAULT '',  -- 2-я колонка (классы/абилки)
    status   VARCHAR(8)    NOT NULL DEFAULT '',  -- ведущий ✅/🟡/⬜/➖
    note     TEXT                                -- полный текст ячейки статуса
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Абилки классов (docs/classes/<класс>-abilities.md).
CREATE TABLE IF NOT EXISTS project.ClassAbilities (
    id          INT AUTO_INCREMENT PRIMARY KEY,
    class       VARCHAR(32)  NOT NULL DEFAULT '',
    tab         VARCHAR(64)  NOT NULL DEFAULT '',  -- «Общее» / школа-специализация
    ability     VARCHAR(255) NOT NULL DEFAULT '',
    spell_id    VARCHAR(64)  NOT NULL DEFAULT '',  -- бывает «5504/587/759», «—»
    school_aura VARCHAR(160) NOT NULL DEFAULT '',
    type        VARCHAR(64)  NOT NULL DEFAULT '',
    status      VARCHAR(8)   NOT NULL DEFAULT '',
    note        TEXT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Таланты классов (docs/classes/<класс>-talents.md). tree = ветка таланта.
CREATE TABLE IF NOT EXISTS project.ClassTalents (
    id        INT AUTO_INCREMENT PRIMARY KEY,
    class     VARCHAR(32)  NOT NULL DEFAULT '',
    tree      VARCHAR(64)  NOT NULL DEFAULT '',   -- Оружие/Неистовство/Защита и т.п.
    talent    VARCHAR(255) NOT NULL DEFAULT '',
    spell_id  VARCHAR(64)  NOT NULL DEFAULT '',
    effect    VARCHAR(255) NOT NULL DEFAULT '',
    type      VARCHAR(64)  NOT NULL DEFAULT '',
    status    VARCHAR(8)   NOT NULL DEFAULT '',
    note      TEXT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Расовые абилки (docs/races/<раса>.md).
CREATE TABLE IF NOT EXISTS project.RacesAbilities (
    id                 INT AUTO_INCREMENT PRIMARY KEY,
    race               VARCHAR(32)  NOT NULL DEFAULT '',
    faction            VARCHAR(16)  NOT NULL DEFAULT '',  -- Альянс/Орда
    ability            VARCHAR(255) NOT NULL DEFAULT '',
    spell_id           VARCHAR(64)  NOT NULL DEFAULT '',
    school_aura_effect VARCHAR(160) NOT NULL DEFAULT '',
    type               VARCHAR(64)  NOT NULL DEFAULT '',
    status             VARCHAR(8)   NOT NULL DEFAULT '',
    note               TEXT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
