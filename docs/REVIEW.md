# Publication review — 2026-10-18

Scope: application source, WinForms controls, single-instance activation,
Task Scheduler integration, driver loading/cleanup, dependencies and packaging.
No application launch, fan-state changes, startup task changes or driver loading
were performed for this review. There is no automated test project.

## Corrected findings

| Finding | Correction |
| --- | --- |
| Manual fan modes could continue without CPU temperature, preventing software overheat protection | All modes return to BIOS when a live CPU temperature is unavailable |
| The driver file could be replaced between checksum verification and loading | Hold the verified file open without write/delete sharing until loading and device opening finish |
| An enabled startup task could silently point to an older application location | Detect the path mismatch and offer repair through the existing Startup toggle |
| An asynchronous startup change could update a disposed window | Check closing/disposed state after the awaited operation |
| The publish profile used a working-directory-sensitive output path | Resolve it relative to the project directory |
| Public packages lacked explicit license scope and source/notice packaging | Add noncommercial application terms, separate dependency attribution, upstream driver sources and runtime-specific notices |

## Remaining limits

- Only the user's OptiPlex 9010 SFF / board 03JR84 / BIOS A30 configuration has
  reported working hardware behavior. Other model/firmware combinations remain
  unverified. There are at most two supported legacy SMM channels.
- The third-party kernel driver is old. Its signature and checksum do not imply
  current Windows security-policy acceptance or a kernel-driver security audit.
  It has not been modified, rebuilt or fuzzed in this review.
- The original driver sources are vendored at a pinned upstream revision. A
  reproducible binary comparison with the signed release has not been performed.
- The .NET 9 target is nearing end of support (10 November 2026). Migrate the
  target, SDK/workflow and packaging requirements to .NET 10 LTS before then.
- Forced termination can prevent restoring BIOS control and removing the driver
  service. Normal-exit cleanup is best effort; firmware behavior is model-specific.
- The global single-instance mutex covers all Windows sessions. Restoring a
  window in another user's/session's desktop is not supported. A normal shortcut
  launch still requests elevation before signaling the existing instance.
- There are no hardware or UI runtime results from this review. Check the release
  checklist manually before publishing binaries. GitHub Actions has been prepared
  locally and has not run against a remote repository.
- Dependency license files remain separate. The application's noncommercial
  restriction does not apply to GPL/MIT components. Separate files alone do not
  determine derivative-work status; changes to that arrangement need review.

Local verification results are recorded in the task response rather than claimed
as hardware test coverage.
