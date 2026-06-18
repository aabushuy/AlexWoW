-- Канбан-доска QA (KB1). Живые данные — CREATE IF NOT EXISTS, БЕЗ DROP (в отличие от project-schema.sql).
-- Применять: cat deploy/sql/kanban-schema.sql | ssh homeserver "docker exec -i alexwow-mysql mysql -ualexwow -palexwow"
-- БД project и права на неё уже созданы (см. шапку project-schema.sql).
--
-- Дерево тикетов (валидация — в сервис-слое, KB2): Проект → только Эпики; Эпик → Задачи/Баги.
--   • type=Project: epic_id IS NULL, project_id IS NULL; дети — только Epic.
--   • type=Epic:    project_id = id проекта, epic_id IS NULL; дети — Task/Bug.
--   • type=Task|Bug: epic_id = id эпика, project_id = проект эпика (денормализация для выборок).
-- tester_guid ссылается на characters.guid в ДРУГОЙ БД (alexwow_auth) — кросс-БД, поэтому без жёсткого FK.

CREATE DATABASE IF NOT EXISTS project CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS project.kanban_ticket (
    id              INT AUTO_INCREMENT PRIMARY KEY,
    title           VARCHAR(255) NOT NULL DEFAULT '',
    description     TEXT,
    test_steps      TEXT,                                   -- Шаги тестирования
    expected_result TEXT,                                   -- Ожидаемый результат
    priority        ENUM('Blocker','Major','Minor')        NOT NULL DEFAULT 'Minor',
    type            ENUM('Task','Bug','Epic','Project')    NOT NULL DEFAULT 'Task',
    status          ENUM('Backlog','Ready to Implementation','In Progress','Testing','Done')
                                                            NOT NULL DEFAULT 'Backlog',
    epic_id         INT NULL,                               -- родительский эпик (для Task/Bug); FK на kanban_ticket.id (signed)
    project_id      INT NULL,                               -- проект (для Epic и денормализованно для Task/Bug)
    assignee        VARCHAR(128) NOT NULL DEFAULT '',       -- Исполнитель
    tester_guid     INT UNSIGNED NULL,                      -- Тестировщик: characters.guid (alexwow_auth, кросс-БД)
    client_check    TINYINT(1)   NOT NULL DEFAULT 0,        -- Проверка на клиенте
    created_at      TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at      TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_kt_status  (status),
    INDEX idx_kt_project (project_id),
    INDEX idx_kt_epic    (epic_id),
    INDEX idx_kt_tester  (tester_guid, client_check),
    CONSTRAINT fk_kt_epic    FOREIGN KEY (epic_id)    REFERENCES project.kanban_ticket(id) ON DELETE SET NULL,
    CONSTRAINT fk_kt_project FOREIGN KEY (project_id) REFERENCES project.kanban_ticket(id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS project.kanban_comment (
    id         INT AUTO_INCREMENT PRIMARY KEY,
    ticket_id  INT NOT NULL,
    author     VARCHAR(128) NOT NULL DEFAULT '',
    body       TEXT NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,   -- сортировка ленты ASC
    INDEX idx_kc_ticket (ticket_id, created_at),
    CONSTRAINT fk_kc_ticket FOREIGN KEY (ticket_id) REFERENCES project.kanban_ticket(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Архивация (KB12). is_archive=1 — тикет скрыт с доски по умолчанию (фильтр «показывать архивные» в UI/API
-- его раскрывает). done_at ставится при переходе в Done, сбрасывается при выходе. Авто-архивация:
-- KanbanArchiveBackgroundService раз в час делает UPDATE WHERE status='Done' AND done_at < NOW()-INTERVAL 2 DAY.
--
-- MySQL 8.4 не поддерживает ALTER … ADD COLUMN IF NOT EXISTS (это MariaDB). Поэтому каждое изменение
-- оборачиваем в проверку INFORMATION_SCHEMA + динамический SQL — повторный прогон не падает.
DROP PROCEDURE IF EXISTS project.__kanban_apply_migrations;
DELIMITER $$
CREATE PROCEDURE project.__kanban_apply_migrations()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                   WHERE TABLE_SCHEMA='project' AND TABLE_NAME='kanban_ticket' AND COLUMN_NAME='is_archive') THEN
        ALTER TABLE project.kanban_ticket ADD COLUMN is_archive TINYINT(1) NOT NULL DEFAULT 0;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                   WHERE TABLE_SCHEMA='project' AND TABLE_NAME='kanban_ticket' AND COLUMN_NAME='done_at') THEN
        ALTER TABLE project.kanban_ticket ADD COLUMN done_at TIMESTAMP NULL DEFAULT NULL;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
                   WHERE TABLE_SCHEMA='project' AND TABLE_NAME='kanban_ticket' AND INDEX_NAME='idx_kt_archive') THEN
        ALTER TABLE project.kanban_ticket ADD INDEX idx_kt_archive (is_archive);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
                   WHERE TABLE_SCHEMA='project' AND TABLE_NAME='kanban_ticket' AND INDEX_NAME='idx_kt_done_at') THEN
        ALTER TABLE project.kanban_ticket ADD INDEX idx_kt_done_at (done_at);
    END IF;
END$$
DELIMITER ;
CALL project.__kanban_apply_migrations();
DROP PROCEDURE project.__kanban_apply_migrations;

-- Метки (KB13). Глобальный словарь имён (нормализация по LOWER в сервис-слое) + many-to-many.
-- Фильтр по нескольким меткам — AND (как в Jira): пересечение тикетов.
CREATE TABLE IF NOT EXISTS project.kanban_label (
    id   INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(64) NOT NULL,
    UNIQUE KEY uk_kl_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS project.kanban_ticket_label (
    ticket_id INT NOT NULL,
    label_id  INT NOT NULL,
    PRIMARY KEY (ticket_id, label_id),
    INDEX idx_ktl_label (label_id),
    CONSTRAINT fk_ktl_ticket FOREIGN KEY (ticket_id) REFERENCES project.kanban_ticket(id) ON DELETE CASCADE,
    CONSTRAINT fk_ktl_label  FOREIGN KEY (label_id)  REFERENCES project.kanban_label(id)  ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
