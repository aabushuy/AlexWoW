-- KB14: пометить regression-тикеты владения оружием (weapon proficiency) меткой `proficiency`.
-- Признак — spell_template.Effect1=25 (SPELL_EFFECT_PROFICIENCY) AND EquippedItemClass=2 (ITEM_CLASS_WEAPON).
-- 15 спеллов: Axes/Maces/Polearms/Swords/Staves/Bows/Guns/Daggers/Thrown/Crossbows/Unarmed/Wands.
-- Эти задачи аддон AlexQATester показывает во вкладке «Общее» (не в «Абилки»).
-- Идемпотентно: INSERT IGNORE по уникальному ключу (ticket_id, label_id).

USE project;

INSERT IGNORE INTO kanban_label (name) VALUES ('proficiency');

INSERT IGNORE INTO kanban_ticket_label (ticket_id, label_id)
SELECT t.id, (SELECT id FROM kanban_label WHERE name = 'proficiency')
FROM kanban_ticket t
WHERE t.project_id = 650
  AND t.title REGEXP '^#[0-9]+ '
  AND CAST(SUBSTRING_INDEX(SUBSTRING(t.title, 2), ' ', 1) AS UNSIGNED) IN (
    SELECT Id FROM mangos.spell_template WHERE Effect1 = 25 AND EquippedItemClass = 2
  );
