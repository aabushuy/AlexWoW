-- БД трекинга прогресса AlexWoW (источник истины для дашборда; перенос из docs/*.md).
-- Применять вместе с project-seed.sql (полный ре-импорт из md — DROP+CREATE+seed):
--   cat deploy/sql/project-schema.sql deploy/sql/project-seed.sql | ssh homeserver "docker exec -i alexwow-mysql mysql -ualexwow -palexwow"
-- ВНИМАНИЕ: DROP TABLE стирает ручные правки. БД — источник истины; ре-импорт запускать осознанно.
-- Создание БД и прав — однократно под root:
--   docker exec alexwow-mysql mysql -uroot -p<rotate-me> -e "CREATE DATABASE IF NOT EXISTS project CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci; GRANT ALL PRIVILEGES ON project.* TO 'alexwow'@'%'; FLUSH PRIVILEGES;"

CREATE DATABASE IF NOT EXISTS project CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

-- Кросс-классовые механики (docs/classes/mechanics.md §2).
DROP TABLE IF EXISTS project.Mechanics;
CREATE TABLE project.Mechanics (
    id       INT AUTO_INCREMENT PRIMARY KEY,
    phase    VARCHAR(128) NOT NULL DEFAULT '',  -- «Фаза 2 — …»
    section  VARCHAR(512) NOT NULL DEFAULT '',  -- «3. Митигейшн …»
    item     VARCHAR(512) NOT NULL DEFAULT '',  -- 1-я колонка (аура/что)
    classes  VARCHAR(512) NOT NULL DEFAULT '',  -- 2-я колонка (классы/абилки)
    status   VARCHAR(8)   NOT NULL DEFAULT '',  -- ведущий ✅/🟡/⬜/➖
    note     TEXT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Абилки классов (docs/classes/<класс>-abilities.md).
DROP TABLE IF EXISTS project.ClassAbilities;
CREATE TABLE project.ClassAbilities (
    id          INT AUTO_INCREMENT PRIMARY KEY,
    class       VARCHAR(48)  NOT NULL DEFAULT '',
    tab         VARCHAR(128) NOT NULL DEFAULT '',  -- «Общее» / школа-специализация
    ability     VARCHAR(512) NOT NULL DEFAULT '',
    spell_id    VARCHAR(64)  NOT NULL DEFAULT '',  -- бывает «5504/587/759», «—»
    school_aura VARCHAR(512) NOT NULL DEFAULT '',
    type        VARCHAR(64)  NOT NULL DEFAULT '',
    status      VARCHAR(8)   NOT NULL DEFAULT '',
    note        TEXT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Таланты классов (docs/classes/<класс>-talents.md). tree = ветка таланта.
DROP TABLE IF EXISTS project.ClassTalents;
CREATE TABLE project.ClassTalents (
    id        INT AUTO_INCREMENT PRIMARY KEY,
    class     VARCHAR(48)  NOT NULL DEFAULT '',
    tree      VARCHAR(128) NOT NULL DEFAULT '',   -- Оружие/Неистовство/Защита и т.п.
    talent    VARCHAR(512) NOT NULL DEFAULT '',
    spell_id  VARCHAR(64)  NOT NULL DEFAULT '',
    effect    VARCHAR(512) NOT NULL DEFAULT '',
    type      VARCHAR(64)  NOT NULL DEFAULT '',
    status    VARCHAR(8)   NOT NULL DEFAULT '',
    note      TEXT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Расовые абилки (docs/races/<раса>.md).
DROP TABLE IF EXISTS project.RacesAbilities;
CREATE TABLE project.RacesAbilities (
    id                 INT AUTO_INCREMENT PRIMARY KEY,
    race               VARCHAR(48)  NOT NULL DEFAULT '',
    faction            VARCHAR(16)  NOT NULL DEFAULT '',  -- Альянс/Орда
    ability            VARCHAR(512) NOT NULL DEFAULT '',
    spell_id           VARCHAR(64)  NOT NULL DEFAULT '',
    school_aura_effect VARCHAR(512) NOT NULL DEFAULT '',
    type               VARCHAR(64)  NOT NULL DEFAULT '',
    status             VARCHAR(8)   NOT NULL DEFAULT '',
    note               TEXT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
