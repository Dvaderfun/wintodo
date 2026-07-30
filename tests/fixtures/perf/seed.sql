WITH RECURSIVE sequence(value) AS (
    SELECT 1
    UNION ALL
    SELECT value + 1
    FROM sequence
    WHERE value < @listCount
)
INSERT INTO lists(
    uid, display_name, color, sort_order, sort_mode, sort_descending,
    show_completed, completed_expanded, is_deleted, created_utc, modified_utc)
SELECT
    printf('perf-list-%02d', value),
    printf('Performance %02d', value),
    (value - 1) % 12,
    value * 1024,
    0,
    0,
    1,
    0,
    0,
    '2026-01-01T00:00:00.0000000Z',
    '2026-01-01T00:00:00.0000000Z'
FROM sequence;

WITH RECURSIVE sequence(value) AS (
    SELECT 1
    UNION ALL
    SELECT value + 1
    FROM sequence
    WHERE value < @taskCount
)
INSERT INTO tasks(
    uid, list_id, summary, status, priority, sort_order,
    created_utc, modified_utc)
SELECT
    printf('perf-task-%05d', value),
    ((value - 1) % @listCount) + 1,
    printf('Performance task %05d', value),
    0,
    CASE WHEN value % 10 = 0 THEN 1 ELSE 0 END,
    value * 1024,
    '2026-01-01T00:00:00.0000000Z',
    '2026-01-01T00:00:00.0000000Z'
FROM sequence;

INSERT INTO settings(key, value)
VALUES ('shell.navigation.selected', 'list:perf-list-01')
ON CONFLICT(key) DO UPDATE SET value = excluded.value;
