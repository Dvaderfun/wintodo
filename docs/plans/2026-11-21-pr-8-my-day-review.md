# Review of PR #8: due-today tasks in My Day

[PR #8](https://github.com/Dvaderfun/wintodo/pull/8) by `ramarahul`, reviewed at commit `6a67c4a`.

The proposal would put tasks due today into **My Day** automatically, using the computer's local date. Manually adding a task still works. If you remove a task that was added automatically, it stays out for the rest of that day and keeps its due date. You can add it back immediately. Tomorrow, the old dismissal no longer applies. The sidebar count and the detail pane would follow the new membership rule.

To remember dismissals across restarts, the PR adds a SQLite column, `my_day_excluded_date`, in forward migration 006. The value is local and is not exported to iCalendar. The PR adds tests for due dates, dismissal and re-adding, undo, and schema upgrades. The author reports 709 unit tests passing and the UI gate pending. GitHub showed no checks on this PR head. Locally, 33 tests from the PR checkout passed in the affected Data suites.

**Recommendation: request changes before merging.**

1. **Preserve the dismissal when accepting a CalDAV change.** A temporary SQLite regression reproduced this: dismiss a task due today, then choose **Keep Theirs** for a server-side rename that leaves the due date alone. The accepted server version clears the local dismissal, so the task returns to My Day that day. Ordinary pulls preserve the new field, but the [conflict path](https://github.com/Dvaderfun/wintodo/blob/6a67c4a942d61bc5623167ad2b3bb8b5a3e27c0c/src/Wintodo.Data/SqliteTaskStore.MoveChoices.cs#L119-L130) and the [cross-list remote move path](https://github.com/Dvaderfun/wintodo/blob/6a67c4a942d61bc5623167ad2b3bb8b5a3e27c0c/src/Wintodo.Data/SqliteTaskStore.Moves.cs#L387-L398) replace the local row without carrying it forward. Preserve local state in both paths and add regressions.
2. **Use the same rule for the row label and verify real UI behavior.** The [task-row subtitle](https://github.com/Dvaderfun/wintodo/blob/6a67c4a942d61bc5623167ad2b3bb8b5a3e27c0c/src/Wintodo.Core/Presentation/TaskMetadataLine.cs#L42-L45) still looks only for any manually stored My Day date. It omits the badge for automatically included tasks and can still show it for a manual addition from yesterday. Verify the row, detail and sidebar together in WintodoTest, including dismissal, undo and midnight rollover.

Migration 006 is forward only. Once a DEV database migrates, the current signed DEV .188, which supports schema 5, will reject that database. Verify the upgrade and backup on a disposable copy before involving the installed DEV profile. The PR was left open and was not merged into DEV.

The synthetic review test and its result summary are in
`artifacts/nov21/pr8-review-evidence/` under the main checkout.
