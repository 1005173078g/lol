# Local protocol capability matrix

Latest probe inspection time: 2026-07-16 13:04 +08:00, while the user reported champion select or loading. Evidence came from a read-only process-name listing, client-declared lockfile presence/shape checks, and an installation configuration-category/file-name listing. No process command lines, configuration contents, paths, endpoints, ports, credentials, or values were recorded. The probe keeps credentials and raw responses in memory, uses normal TLS certificate validation, and writes only redacted JSON shapes.

| Capability | Status | Source process | Observed phase | Redacted field paths / evidence |
| --- | --- | --- | --- | --- |
| League game phase | unavailable / unverified | `LeagueClientUx` and `LeagueClient` appeared in the process-name listing; the readable client location exposed only an empty/stale lockfile and no usable connection declaration | User reported champion select or loading; API phase could not be verified | No response shape was available. |
| League participants, team mapping, player identifiers, and champions | unavailable / unverified | Same process and empty/stale lockfile evidence as phase probing | User reported champion select or loading; API participant visibility could not be verified | No response shape was available; enemy identity visibility must not be assumed. |
| LCU recent ranked history used by the WeGame adapter | **available** | The single `LeagueClientUx` process's WMI-declared loopback session; credentials remained in memory | CN runtime verified on 2026-07-16 outside a game | Full Riot ID lookup and history returned HTTP 200 under strict certificate pinning. History contained 20 `games.games[]` entries. Confirmed shapes: `gameCreation:number`, `queueId:number`, `participants:array` (one item), `participants[].championId:number`, and `participants[].stats.{win:boolean,kills:number,deaths:number,assists:number}`. No values or identifiers were recorded. |

## Task 4 contract status (2026-07-16)

| Capability | Contract status | Runtime status in CN client | Allowed fields / behavior |
| --- | --- | --- | --- |
| Live Client player list | **Official contract verified** from Riot's Game Client / Live Client Data API documentation: <https://developer.riotgames.com/docs/lol> | **CN runtime unverified** in this run; no claim of a successful live response | `GET https://127.0.0.1:2999/liveclientdata/playerlist`; each player uses official `team` (`ORDER`/`CHAOS`), `championName`, `riotIdGameName`, and `riotIdTagLine`. The official playerlist contract does not expose champion ID; the adapter resolves it through a separate champion catalog. |
| Active player identity | **Official contract verified** from the same Riot documentation | **CN runtime unverified** | `GET https://127.0.0.1:2999/liveclientdata/activeplayername`; used to identify the local player's explicit team before enemy mapping. |
| Champion-select participants | Unsupported/unverified LCU response shape | **CN runtime unverified** | No participant DTO or endpoint is assumed. The adapter returns `ParticipantsUnavailableException` and waits for the official Live Client API. |
| LCU game-flow phase | User-approved local-client route; LCU is explicitly unsupported by Riot for third-party stability | **CN runtime unverified** | Used only for phase selection. Unknown values fail closed as `ProtocolChangedException`. |

The sanitized fixture is hand-authored from the official playerlist field contract and contains only fictional Riot IDs. It is not captured runtime data.

## Task 5 contract status (2026-07-16)

| Capability | Contract source | CN runtime status | Adapter behavior |
| --- | --- | --- | --- |
| Summoner lookup | Lytical's reviewed open-source LCU usage: `/lol-summoner/v1/summoners?name=...`; this is an unsupported LCU contract, not an official Riot third-party API | Verified HTTP 200 only when `name` is the URL-encoded full `gameName#tagLine`; game name alone returned HTTP 422 | Reads only required `puuid:string`; missing/wrong type fails closed as `ProtocolChangedException`. |
| Match history | Lytical's reviewed open-source LCU usage: `/lol-match-history/v1/products/lol/{puuid}/matches?begIndex=0&endIndex=19`; unsupported LCU contract | Verified HTTP 200 and the required redacted shape listed above | Parses only `games.games[]` and required match/participant/stat fields. Unknown fields in the large LCU objects are ignored, while missing/wrong required subtrees fail closed. |
| Ranked queue classification | Riot queue identifiers: 420 Ranked Solo/Duo and 440 Ranked Flex | `queueId:number` shape verified; values deliberately not recorded by the probe | Only queue IDs 420 and 440 are accepted; results are sorted by `gameCreation` descending and capped at 20. |
| MVP and position | No official or reviewed open-source field contract approved for the observed response | No matching runtime field path confirmed | MVP is `null`; position is `""`. KDA is never used to infer either field. |

The Task 5 sanitized fixture is hand-authored from the reviewed contract and the value-free CN runtime shape. It contains only fictional identifiers and is not captured runtime data.

## Safety boundaries

- League requests are limited to HTTPS loopback. The LCU port and token come only from the single running `LeagueClientUx` process's self-declared command line. Certificate validation is never disabled.
- Task 5 uses only the same WMI-declared `LeagueClientUx` HTTPS loopback session and strict per-request certificate pinning as Task 4. It does not connect to a WeGame remote host.
- Authentication headers, cookies, tokens, tickets, session material, player identifiers, query parameters, raw URLs, and raw responses are never written to the console, artifacts, documentation, or source control.
- A successful probe may create only `artifacts/probe/<command>.shape.json`; these files contain allowlisted static protocol field names, JSON types, array lengths, and the first array item's shape. Sensitive, unknown, dynamic, or duplicate field names become stable per-object keys such as `redacted-field-1`; their original names and values are discarded.
- Historical note: the initial local probe did not authorize adapters because it obtained no response contract. Task 4 was authorized by the later user decision plus Riot's official Live Client contract. Task 5 was authorized by the reviewed open-source LCU contract and subsequently verified against the value-free CN runtime shapes above.
