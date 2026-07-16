# Local protocol capability matrix

Probe date: 2026-07-16. The probe is read-only, keeps credentials and raw responses in memory, uses normal TLS certificate validation, and writes only redacted JSON shapes.

| Capability | Status | Source process | Observed phase | Redacted field paths / evidence |
| --- | --- | --- | --- | --- |
| League game phase | unavailable / unverified | `LeagueClientUx` or `LeagueClient` | League client not running | No response shape was available. |
| League participants, team mapping, player identifiers, and champions | unavailable / unverified | `LeagueClientUx` or `LeagueClient` | League client not running | No response shape was available; enemy identity visibility must not be assumed. |
| WeGame current-player recent ranked history | unavailable / unverified | `wegame` | WeGame running; no verified local HTTPS endpoint declared by inspected local configuration | Ranked list, result, MVP, position, champion, and KDA field paths remain unverified. |

## Safety boundaries

- League requests are limited to HTTPS loopback addresses derived from the running client's local lockfile. Certificate validation is never disabled.
- WeGame requests are not attempted unless a local configuration explicitly declares a validated HTTPS host. No such endpoint was verified in this run.
- Authentication headers, cookies, tokens, tickets, session material, player identifiers, query parameters, raw URLs, and raw responses are never written to the console, artifacts, documentation, or source control.
- A successful probe may create only `artifacts/probe/<command>.shape.json`; these files contain property names, JSON types, array lengths, and the first array item's shape. Sensitive field names matching the redactor policy have the value `redacted-field`.
- These results do not authorize Task 4 or Task 5 adapters. Re-run the probe with the relevant client active before defining DTOs or protocol mappings.
