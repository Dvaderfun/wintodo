# Diagnostic privacy

App failure traces use a constant `WINTODO_*` operation label followed by
`DiagnosticException.Describe`. The Core helper emits an allowlisted type code
and, where available, an HTTP status from 100 to 599. It never evaluates an
exception's message, custom type name, stack trace, inner exception, Data or
Source. Provider descriptions, Graph service messages, task text, account names,
emails, URLs and paths must not be interpolated into app traces.

The inventory covers startup/unhandled failure and notification activation;
ShareTarget/reporting; sign-in callbacks; import/onboarding/staging cleanup;
iCalendar transfer and temporary cleanup; sync results/status/UI; attachment
pickers; preferences, appearance and database settings; navigation, task saving
and undo; Windows reminder/badge/jump-list refresh. Existing safe stage, count,
duration, enum and byte-count traces remain. Sync failures show localized retry
guidance, never raw service text. Identity-provider descriptions are discarded
when sign-in fails; error category still distinguishes cancellation.

Debug alone accepts `--trace-file`. Release app traces use the same safe
formatting but do not install that durable file listener. Native binding
tracing is disabled: its raw native output cannot be sanitized by a
`BindingFailed` handler. Compiler/analyzer and guest UI checks still apply.

CI uploads only the fixed-name/count test summary from
`Export-SafeTestResults.ps1`. Raw TRX and coverage remain private. The exporter
prohibits DTD/external resolution, bounds XML document size, checks counters,
rejects duplicate/malformed/incomplete reports and removes stale successful
output before parsing. It preserves failures, skips and unresolved counts.

Regression proof includes synthetic token/password/email/task/account/path
markers in exception messages, nested exceptions and metadata; hostile custom
Message/ToString overrides; HTTP status bounds; real sync service failures and
their status events; and marked/malformed test-result exports. The import guest
gate additionally scans its synthetic fixture traces for private values.

Verify the Release branch in the guest with the built Release JIT layout and
the existing synthetic schema-guard seed:

```powershell
.\scripts\Invoke-VmGate.ps1 -Gate run-release-diagnostics-gate.ps1 -Include `
  'src\Wintodo.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64', `
  'tests\ui\artifacts\schema-guard\seed.db'
```

This checks actual startup/error behavior and that a writable `--trace-file`
target stays absent. Run `DiagnosticExceptionTests` in Release separately to
verify safe formatting. These checks do not capture external debugger output
or establish Native AOT/final-package evidence.

Scope limits: external debugger output, dumps, build logs, raw test results and
local VM provisioning logs are private diagnostic material. The app helper
cannot sanitize output emitted by those tools or the OS. Final package binary
paths, publish summaries and release provenance belong to DEP-14; comprehensive
release artifact handling belongs to DEP-15/18/19. Passing these regressions
does not certify those future release gates. IMP-25's complete expanded import
gate (25/25) and individual capture review passed on 2026-11-08; Release formatter
9/9 and the Release JIT guest gate 6/6 passed separately.
