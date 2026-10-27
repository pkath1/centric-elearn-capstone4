---
name: spec-to-tests
description: Turn the AC-nn Given/When/Then acceptance criteria in a docs/specs spec file into xUnit tests in backend/Larchmont.Tests, named AC_nn_<Behaviour>. Use when a spec exists and its tests are missing.
---

# Spec to tests

Source: a spec file in `docs/specs/` based on `docs/specs/_template.md`. Acceptance criteria are lines like `- AC-01 Given ... When ... Then ...`.

## Steps
1. Read the spec's Business rules and Acceptance Criteria. Skip any AC that has no Given/When/Then.
2. For each AC, add one `[Fact]` in `backend/Larchmont.Tests`. Put service-level criteria in the service's test class in `UnitTests.cs` (Moq for repositories). Put HTTP criteria in `ApiTests.cs` (`ApiFactory`, InMemory).
3. Name the method `AC_<nn>_<Behaviour>`, for example `AC_01_RejectsUnknownRoomStatus`. Use the AC number and short PascalCase words for the behaviour.
4. Write the body in three commented blocks: `// Given`, `// When`, `// Then`, mirroring the AC text.
5. Do not test dates against today's date. Build dates from fixed values.
6. Run `cd backend && dotnet test Larchmont.sln`. Failing tests for unimplemented behaviour are expected only if the spec says the behaviour is not built yet; say which.

## Checks
- Every AC in the spec has exactly one test, or the summary lists the ones skipped and why.
- Test names contain the AC number, so `AC_01` matches spec line `AC-01`.
- No test needs a MySQL server.
