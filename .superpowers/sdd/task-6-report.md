# Task 6 implementation report

## Takeover

Recovered the previous agent's uncommitted work in `LolScout.sln`, `Ports.cs`, `src/LolScout.App`, and `tests/LolScout.App.Tests` without discarding it. The inherited tests reproduced a race: the fake clock completed delays after `Task.Yield`, allowing polling to flood ahead before assertions observed the intended phase.

## Result

- Implementation commit: `19e0d9c1146149207a0ea27184438c19de79abe9` (`feat: orchestrate live opponent scouting`).
- Added an application coordinator that serially publishes phase/query states through a channel.
- Match fingerprints normalize and sort the five opponent identities with champion IDs and team ID.
- Each batch starts five player requests concurrently; one failure becomes a per-player error and does not fail the other results.
- `Ended` and watcher cancellation cancel the active batch and clear the roster/fingerprint.
- `RefreshAsync` reuses the current roster, cancels the old batch, retains the automatic dedupe fingerprint, and uses a monotonically increasing batch number so stale results cannot publish.
- Replaced the flooding fake clock with a controlled scheduler: every delay is queued and tests explicitly advance one expected interval. No real sleeps are used.

## Verification

- `dotnet test tests\LolScout.App.Tests\LolScout.App.Tests.csproj --filter MatchScoutCoordinatorTests --no-restore`
  - PASS: 4 passed, 0 failed, 0 skipped (30 ms on final directed run).
- `dotnet test LolScout.sln -c Release --no-restore`
  - PASS: Core 17/17, Infrastructure 46/46, App 4/4; total 67 passed, 0 failed, 0 skipped.
- `git diff --check`
  - PASS: exit code 0; only Git CRLF conversion notices were emitted during later staging.

## Review notes

- The batch number guards both incremental `Querying` updates and final `Complete`, while cancellation handles cooperative sources. Together these prevent an old refresh batch from overwriting a newer one even if a source returns late.
- Tests cover the required ordered state subsequence, same-match automatic dedupe, five-way concurrency, isolated failure, deterministic `Ended` cancellation/fingerprint clearing, and refresh cancellation/stale-result suppression.

## Review remediation

- Replaced the split field lock/batch startup sequence with one asynchronous `SemaphoreSlim` state gate. Refresh, discovery batch creation, generation increments, cancellation, and Ended/Waiting reset now share that gate.
- Every batch checks generation plus cancellation before initial `Querying`, before each source request, before every incremental result, and before `Complete`.
- Manual refresh uses an `IClock` zero-duration scheduling point. Concurrent refresh calls establish their generations before the fake scheduler releases startup, so only the latest generation starts or publishes; this removed a Release-only race that reproduced as 15 rather than 10 source calls.
- Ended/Waiting increment generation, cancel the batch, and clear roster/fingerprint inside the gate before publishing an empty state. A test advances another Ended poll and verifies no later request starts.
- Replaced the shared lifetime channel with a per-watch channel. One UI watcher is allowed at a time; a concurrent second watcher deterministically throws `InvalidOperationException`, and a new watcher is allowed after disposal of the first.
- Added direct fingerprint normalization/order coverage and non-cooperative stale result and stale exception sources to verify neither can leak after refresh.
- All two-second waits are deadlock guards around controllable completion sources/semaphores, not scheduling sleeps.

### Review verification

- `dotnet test tests\LolScout.App.Tests\LolScout.App.Tests.csproj --filter MatchScoutCoordinatorTests --no-restore`
  - PASS: 10 passed, 0 failed, 0 skipped (99 ms).
- `dotnet test tests\LolScout.App.Tests\LolScout.App.Tests.csproj -c Release --filter MatchScoutCoordinatorTests --no-restore`
  - PASS: 10 passed, 0 failed, 0 skipped (107 ms).
- `dotnet test LolScout.sln -c Release --no-restore`
  - PASS: Core 17/17, Infrastructure 46/46, App 10/10; total 73 passed, 0 failed, 0 skipped.

## Second review remediation

- Removed the per-player check/call TOCTOU. `RunBatchAsync` now holds the single state gate while it validates generation/token, publishes initial `Querying`, and synchronously invokes all five source methods to capture their tasks. Ended/Refresh can therefore observe only an all-not-started or all-started batch boundary. Awaiting and result publication remain outside/through the gate respectively.
- Added a synchronous first-start barrier test. Ended is requested while the first source invocation holds the gate; it cannot publish until all five tasks have been obtained, then cancels the already-started batch. The test verifies exactly five starts and an empty Ended roster.
- Replaced `Delay(0)`/`Task.Yield` refresh coalescing with a positive 10 ms `IClock.DelayAsync` debounce. Concurrent refresh generations are created before the controlled fake clock releases debounce; only the latest starts and publishes.
- Poll failures now complete the active watch channel with the exception. Watch cleanup releases the single-watcher lease in a `finally`, including when polling faults. Phase and participant failure tests verify propagation and successful watcher restart.
- Expanded fingerprint tests to cover team changes, champion changes, and mixed-team rejection.

### Second review verification

- `dotnet test tests\LolScout.App.Tests\LolScout.App.Tests.csproj --filter MatchScoutCoordinatorTests --no-restore`
  - PASS: 14 passed, 0 failed, 0 skipped (85 ms).
- `dotnet test LolScout.sln -c Release --no-restore`
  - PASS: Core 17/17, Infrastructure 46/46, App 14/14; total 77 passed, 0 failed, 0 skipped.
