# Sati — Human Services Infrastructure for Maine

Sati is a Maine-focused case-management platform built from daily human-services work. It is one
program on SatiLogica, owned and operated by RobinBradleyAMS. Use the formal spelling SatiLogica
in code, documentation and user-facing text; DNS resource names remain lowercase.

The Windows WPF client has two deliberately distinct operating paths: API-mediated synthetic Demo
and Josh's personal local working database. Future cloud Production is not deployed. The current
architecture supports documentation, clinical review, scoped supervision and early billing work;
actual payer acceptance, regulatory compliance and production readiness must not be inferred.

## Documentation owners

| Question | Canonical owner |
|---|---|
| What rules must an assistant follow? | [AGENTS.md](AGENTS.md); [CLAUDE.md](CLAUDE.md) is a pointer |
| What owns what today? | [ARCHITECTURE.md](ARCHITECTURE.md), with current feature references |
| Why was a choice made; what superseded it? | [DECISIONS.md](DECISIONS.md), dated decision index |
| What work remains? | [AGENDA.md](AGENDA.md), stable backlog IDs |
| What is source-complete, enabled, evidenced or blocked? | [Readiness registry](docs/readiness/README.md) |
| What was actually observed in each environment? | [DATABASE_ENVIRONMENTS.md](DATABASE_ENVIRONMENTS.md), dated inventory |
| How are operations performed safely? | [OPERATIONS.md](OPERATIONS.md) |
| How is a release authorized and verified? | [RELEASE_PLAYBOOK.md](RELEASE_PLAYBOOK.md) |
| Who can access each API route? | [API_AUTHORIZATION.md](API_AUTHORIZATION.md) |
| What audit evidence is retained? | [AUDIT_EVENTS.md](AUDIT_EVENTS.md) |
| What external/regulatory decisions are required? | [REGULATORY_CONCERNS.md](REGULATORY_CONCERNS.md) |
| Where are other topic owners and historical evidence? | [Documentation manifest](docs/documentation-index.json), [archive](archive/README.md) |

[Documentation governance](docs/documentation-governance.md) defines ownership, link preservation,
explicit supersession and required validation. Dated audits describe their reviewed snapshot;
they are evidence, not a certification or current implementation instruction.

## Continuing work

Use `Perform the next thing on the list` as the entire message to start the bounded slice selected
by [the agenda's next eligible pointer](AGENDA.md#next-eligible-work). The assistant follows the
[standing workflow](AGENTS.md#standing-work-and-documentation-upkeep), reports the concrete scope,
and updates the canonical documentation and [working evidence](docs/readiness/work-evidence.md)
after each significant work portion. Release and environment actions keep their separate rules.

## Development

The client uses .NET 10, WPF, CommunityToolkit.Mvvm, EF Core and SQL Server. Windows desktop
development requires the .NET 10 SDK, appropriate Visual Studio workload and SQL Server LocalDB.
`SatiLogica.slnx` is the repository solution; `Sati.csproj` is the WPF product project.

```powershell
dotnet build SatiLogica.slnx
dotnet test SatiLogica.slnx
dotnet run --configuration Debug
```

The first window selects the environment before any data connection. Do not inspect personal
working data as part of development without explicit authorization. Consult the canonical
environment and workstation policies before migrations, exports, logs, backups or crash captures.

Historical release versions/test totals previously copied here are retained in the
[byte-preserved README snapshot](docs/archive/2026-10-08/README.md). Current facts belong in their
named owners instead of being repeated here.
