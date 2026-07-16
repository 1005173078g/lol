# Task 5 implementation report

## Outcome

- Implemented `IRecentMatchSource` through the WMI-declared `LeagueClientUx` LCU session.
- Uses strict per-request certificate pinning inherited from Task 4, with bounded retries and per-attempt timeout.
- Resolves a full Riot ID (`gameName#tagLine`) to PUUID, reads the last 20 history records, filters queue IDs 420/440, sorts newest first, and maps the confirmed match fields.
- Leaves unconfirmed MVP and position fields as `null` and `""`; it does not infer either from KDA.

## CN runtime capability evidence

The administrator read-only probe reused the production WMI discovery and certificate-pinned transport. No credential, player identifier, query parameter, URL, or raw response was printed or persisted.

- Fixed two Task 4 runtime defects found by the probe: whole-argument WMI quoting and issuer validation against a simple name rather than the full issuer DN.
- Certificate checks after the fix: fingerprint, subject, issuer, and validity all passed.
- Full Riot-ID summoner lookup: HTTP 200.
- Match history: HTTP 200; 20 records.
- Confirmed value-free shapes: `games.games[]`, `gameCreation:number`, `queueId:number`, one `participants[]` item, `championId:number`, and `stats.win:boolean` / `kills:number` / `deaths:number` / `assists:number`.
- One cold history request took about 23.9 seconds; later requests took about 0.4 seconds, and the full lookup plus history took about 2.9 seconds. The adapter retains the approved four-second per-attempt timeout and one retry, so cold-start failure is bounded rather than hanging indefinitely. A later background refresh can retry at workflow level.

## Verification

- `dotnet test tests/LolScout.Infrastructure.Tests/LolScout.Infrastructure.Tests.csproj -c Release --filter WeGameRecentMatchSourceTests`: 8 passed, 0 failed.
- `dotnet test -c Release`: 50 passed, 0 failed (11 Core + 39 Infrastructure).
- `git diff --check`: clean (line-ending notices only).
- Credential-pattern scan found only pre-existing, explicitly fictional command-line tokens in discovery tests and the documented scan command itself; no real credential values or captured responses were added.

## Safety notes

- Connections are restricted to HTTPS IPv4 loopback.
- Credentials remain in mutable character buffers and are cleared on disposal.
- 401/403 map immediately to `WeGameNotSignedInException` without retry.
- Only connection failures, per-attempt timeout, and 408/429/502/503/504 retry once after 250 ms.
- Required subtrees and types fail closed as `ProtocolChangedException`; unrelated fields in large LCU objects are ignored.

## Review follow-up

- History parsing now validates `gameCreation` and `queueId` first, skips non-ranked records, and only then strictly parses the ranked participant/stat projection. A queue 400 record without participant data is accepted and skipped; the equivalent queue 420 shape fails closed.
- `PlayerIdentity` now rejects null/blank game name, tag line, and region with `ArgumentException`, and trims all three components. The adapter also rejects a null/incomplete identity before session discovery or transport use, preventing a `gameName#` request.
- Caller cancellation is propagated without retry; a focused test verifies exactly one underlying attempt.
- Ranked records require exactly one participant because the approved CN runtime shape is an LCU single-player projection. Both zero and multiple participants map to `ProtocolChangedException`.
