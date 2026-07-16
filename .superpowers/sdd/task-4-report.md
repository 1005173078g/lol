# Task 4 implementation report

## Status

Task 4 review fixes are implemented. Task 5 was not changed. The report's commit is the SHA returned by `git rev-parse HEAD` after commit.

## Contract and runtime evidence

- Primary participant contract: Riot's official Game Client / Live Client Data API documentation at <https://developer.riotgames.com/docs/lol>.
- Official endpoints used: `/liveclientdata/activeplayername` and `/liveclientdata/playerlist` on `https://127.0.0.1:2999`.
- Official player fields used: `team` (`ORDER` or `CHAOS`), `championName`, `riotIdGameName`, and `riotIdTagLine`. Champion ID is resolved through a separate injected champion catalog because the official playerlist contract does not contain it.
- CN runtime remains unverified. No real-match API success is claimed. The fixture is hand-authored from the official contract and contains fictional IDs only.
- Champion-select participant data is deliberately unavailable: no unsupported/unverified LCU participant response shape is encoded. The adapter waits for the official Live Client API.

## Security and fail-closed behavior

- Process command lines are read in-process through `System.Management` (`Win32_Process`) behind `IProcessCommandLineSource`; no PowerShell process or stdout is used.
- Discovery accepts exactly one `LeagueClientUx` process. Zero or multiple matches fail without including command lines or tokens in exceptions.
- Token handling is minimized and memory-only: it is never cached by `LeagueSession`, logged, included in exceptions, or exposed by `ToString()`. The disposable connection clears its owned `char[]` after every LCU call. The WMI `CommandLine`, temporary parser string, .NET authentication header, and runtime HTTP internals necessarily create short-lived immutable string copies that cannot be zeroed; the implementation keeps none in fields beyond the immediate request and disposes the request, header owner, client, handler, and connection promptly.
- Requests are restricted to HTTPS IPv4 loopback.
- TLS checks an exactly 32-byte SHA-256 pin, parsed subject simple name `rclient`, parsed issuer name containing `Riot Games`, and validity dates. Every request owns a separate handler, callback state, and client; there is no shared pin-failure flag. The callback returns false rather than throwing, and only that request's resulting HTTP failure maps to `CertificatePinMismatchException`.
- Player mapping uses `OrdinalIgnoreCase` for Riot ID matching and requires exactly ten unique players, a uniquely identified active player, exactly five players per explicit `ORDER`/`CHAOS` side, and exactly five enemies. Hidden identities and known-but-unavailable structures fail as `ParticipantsUnavailableException`; empty or unknown team enum values fail as `ProtocolChangedException`.
- JSON uses `UnmappedMemberHandling.Disallow`. Unknown properties, missing required fields, duplicate identities, and unknown champions fail closed.

## Fresh verification

- `dotnet test -c Release --no-restore --filter LeagueSessionTests`
  - PASS: 20, FAIL: 0, SKIP: 0. This includes the concurrent pin-failure/ordinary-connection-failure isolation test.
- `dotnet test -c Release --no-restore`
  - Core PASS: 11, FAIL: 0.
  - Infrastructure PASS: 28, FAIL: 0.
  - Total PASS: 39, FAIL: 0.
- `$unsafe = rg -n "powershell|DangerousAcceptAnyServerCertificateValidator|ServerCertificateCustomValidationCallback\\s*=.*=>\\s*true|https?://(?!127\\.0\\.0\\.1)|static\\s+.*pinRejected|private\\s+.*pinRejected" src/LolScout.Infrastructure/League tests/LolScout.Infrastructure.Tests/LeagueSessionTests.cs -i -P; if ($LASTEXITCODE -eq 0) { $unsafe; exit 1 } elseif ($LASTEXITCODE -ne 1) { exit $LASTEXITCODE }`
  - PASS with no matches: no PowerShell, accept-any callback, non-loopback URL, or shared pin-rejection field.
- `git diff --check`
  - PASS; only line-ending conversion notices were emitted.

## Remaining attention

- Re-run a read-only, shape-only runtime probe during a future CN match before claiming runtime compatibility. Never persist raw responses or credentials.
- Certificate rotation must remain a manual trust update after independent certificate identity verification; do not weaken validation.
- The final executable host still needs the already-approved require-administrator manifest when that host project is added.
