# Local protocol capability matrix

Probe inspection time: 2026-07-16 12:28 +08:00. Evidence came from a read-only process-name listing and an installation configuration-category/file-name listing. No process command lines, configuration contents, paths, endpoints, or values were recorded. The probe keeps credentials and raw responses in memory, uses normal TLS certificate validation, and writes only redacted JSON shapes.

| Capability | Status | Source process | Observed phase | Redacted field paths / evidence |
| --- | --- | --- | --- | --- |
| League game phase | unavailable / unverified | Expected `LeagueClientUx` or `LeagueClient`; neither appeared in the process-name listing | League client not running | No response shape was available. |
| League participants, team mapping, player identifiers, and champions | unavailable / unverified | Expected `LeagueClientUx` or `LeagueClient`; neither appeared in the process-name listing | League client not running | No response shape was available; enemy identity visibility must not be assumed. |
| WeGame current-player recent ranked history | unavailable / unverified | `wegame` and `wegame_env` appeared in the process-name listing; installation-level INI/XML and configuration-directory names were inspected without reading or recording values | WeGame running; no configuration-declared HTTPS endpoint was verified | Ranked list, result, MVP, position, champion, and KDA field paths remain unverified. |

## Safety boundaries

- League requests are limited to HTTPS loopback addresses derived from the running client's local lockfile. Certificate validation is never disabled.
- WeGame requests are not attempted unless a local configuration explicitly declares a validated HTTPS host. No such endpoint was verified in this run.
- Authentication headers, cookies, tokens, tickets, session material, player identifiers, query parameters, raw URLs, and raw responses are never written to the console, artifacts, documentation, or source control.
- A successful probe may create only `artifacts/probe/<command>.shape.json`; these files contain allowlisted static protocol field names, JSON types, array lengths, and the first array item's shape. Sensitive, unknown, dynamic, or duplicate field names become stable per-object keys such as `redacted-field-1`; their original names and values are discarded.
- These results do not authorize Task 4 or Task 5 adapters. Re-run the probe with the relevant client active before defining DTOs or protocol mappings.
