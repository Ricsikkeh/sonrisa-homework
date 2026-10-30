# Implementation plan and workflow

## Current status

The MVP decisions are consolidated in [MVP.md](MVP.md). This initial increment saves planning documentation only; no application implementation is authorized by saving these files.

The implementation sequence below is proposed. Before each implementation increment, agree on its scope, acceptance criteria, validation approach, and significant technical decisions.

## Proposed increment 1: Backend foundation and executable API check

### Scope

- Establish a .NET 10 ASP.NET Core API project and one test project.
- Add a minimal public `GET /health` liveness endpoint.
- Test the endpoint through the real ASP.NET Core request pipeline using an in-process host.
- Document local run/test instructions and update planning/validation evidence.

Proposed choices requiring approval: one backend application project with module folders introduced as needed, and xUnit with ASP.NET Core's in-process test host. See [DECISIONS.md](DECISIONS.md).

### Acceptance criteria

1. The backend targets .NET 10 and builds successfully.
2. `GET /health` returns HTTP 200 and a small documented JSON response without sensitive diagnostics.
3. An automated integration test exercises the endpoint through the request pipeline.
4. Local startup and test commands are documented.
5. Documentation distinguishes agreed decisions, deferred details, and production gaps.
6. No business features, unnecessary abstractions, external integrations, or production infrastructure are introduced.

Liveness does not claim database or application readiness. Angular, Material, PostgreSQL, EF Core, Identity, and notification processing remain required but are outside this increment.

### Validation approach

1. Verify the available SDK/toolchain; report missing prerequisites rather than substitute framework versions.
2. Write the endpoint test before implementing the endpoint.
3. Run it and verify the expected failure.
4. Implement the endpoint and rerun the test.
5. Run the backend build and full available test suite.
6. Start the API and perform a direct HTTP smoke check.
7. Report actual results and limitations, then stop for review.

## Later milestones: proposed sequencing, not increment approval

| Milestone | Deliverable and acceptance focus |
| --- | --- |
| Local stack | Angular/Material frontend, PostgreSQL/EF Core integration, and reproducible local startup, split into small increments. |
| Accounts and access | Registration/login, authorized role boundaries, safe bootstrap, and final-administrator protection. |
| Event types and subscriptions | Admin type management, user destinations/subscriptions, uniqueness, and deletion restrictions. |
| Publication vertical slice | Immutable event publication, publication-time matching/snapshots, and recorded simulated deliveries. |
| Delivery behavior | Deterministic failure simulation, transient-only retries, duplicate suppression, and publication-time destination behavior. |
| User and admin experience | Feature facades, own-history UI, per-user admin inspection, role management, safe summaries, and masked destinations. |
| MVP validation | End-to-end journeys, access-control checks, regression checks, setup instructions, and documented production limitations. |

Milestones may be reordered when planning their increments. They are not permission to implement the whole milestone at once.

## Development workflow

For each approved business-rule/behavior increment:

1. Write the test first.
2. Run it and verify failure for the expected reason.
3. Implement the minimum correct behavior.
4. Run the tests again.
5. Refactor where useful, keeping tests passing.

At the end of each increment:

- Summarize changes.
- Record commands/checks actually run and their results, including red/green evidence.
- Identify limitations, unresolved issues, and production gaps.
- Stop for review.
- Commit only after explicit approval, using a message describing the actual change.
