-- =====================================================================================================
-- EF Core baseline для СУЩЕСТВУЮЩЕЙ прод-БД alexwow_auth (рефактор DAL #23, Срез 2).
--
-- Зачем: таблицы alexwow_auth уже созданы старым EnsureSchemaAsync (CREATE/ALTER), в них живые данные.
-- Миграция 20260609075753_InitialCreate описывает РОВНО эту же схему. Чтобы EF считал её уже применённой
-- (и НЕ пытался пересоздавать таблицы при первом MigrateAsync), регистрируем её в __EFMigrationsHistory
-- вручную — это и есть baseline. CREATE TABLE из InitialCreate на проде НЕ выполняются.
--
-- Когда применять: ОДИН РАЗ на проде, перед первым запуском версии сервера, которая зовёт MigrateAsync
-- (Срез 3/4). На чистой/dev БД baseline НЕ нужен — там MigrateAsync сам создаст таблицы с нуля.
--
-- Как применять (на homeserver):
--   docker exec -i alexwow-mysql mysql -uroot -p<rotate-me> alexwow_auth < deploy/sql/ef-baseline-alexwow_auth.sql
--
-- Идемпотентно: повторный запуск ничего не ломает (INSERT под защитой NOT EXISTS).
-- =====================================================================================================

CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId`    varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `ProductVersion` varchar(32)  CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)
) CHARACTER SET=utf8mb4;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
SELECT '20260609075753_InitialCreate', '9.0.0'
WHERE NOT EXISTS (
    SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260609075753_InitialCreate'
);
