# Project Instructions

## Repository
This is a C#/.NET application.

Before changing code, inspect the relevant implementation and reuse existing
patterns where they fit. Do not copy known defects; keep any necessary departure
focused and explain why it is needed.

## Architecture
- Respect existing component boundaries.
- Keep business logic separate from presentation
  and HTTP handling where practical.
- Use existing dependency injection conventions.
- Avoid unnecessary abstractions or dependencies.

## Changes
- Keep changes focused on the requested task; avoid unrelated refactoring.
- Explain significant architectural trade-offs.
- Ask before adding production dependencies
  or introducing breaking changes, unless the user has already authorized them.

## Verification
- Choose checks appropriate to the change, using the commands below and the
  README's Verification section. Documentation-only changes normally need a
  content and diff review, not an application build or test run.
- For application changes, run the relevant build and .NET tests. For browser
  interaction changes, also run the Playwright suite. Run .NET and browser tests
  sequentially because they share application build output.
- Use the existing temporary-vault test harnesses and mocked external responses;
  do not point automated tests at the user's real Obsidian vault.
- Fix failures introduced by your changes.
- State which checks ran and their results. If a relevant check was skipped or
  blocked, explain why; do not describe unrun checks as passing.
- Do not use live external services for testing
  unless explicitly authorized.

## Completion
Summarize:
- What changed and why.
- What validation was performed.
- Any unresolved issues or limitations.

## Project Structure
- `MarkdownGameTracker`: main application
- `MarkdownGameTracker/Api`: minimal-API endpoints
- `MarkdownGameTracker/Models`: request/response records and `GameStatuses`
- `MarkdownGameTracker/Services`: IGDB, HowLongToBeat, and Markdown rendering
- `MarkdownGameTracker/Storage`: file I/O, serialization, and update policies
- `MarkdownGameTracker/Pages`: Razor Pages
- `MarkdownGameTracker.Tests`: unit and integration tests
- `browser-tests`: Playwright interaction tests

## Development
- From the repository root:
  - Build: `dotnet build MarkdownGameTracker.slnx`
  - Test: `dotnet test MarkdownGameTracker.slnx`
- Browser tests: run `npm test` from `browser-tests` after the .NET tests finish.
  See [README.md — Verification](README.md#verification) for Node.js, Playwright,
  and installed Microsoft Edge setup options.
- Use these commands as the baseline. Flags used in earlier work, such as
  `--no-restore`, Release configuration, or warnings-as-errors, are not mandatory
  unless the current task or repository automation requires them.
- Keep deployment local-only as documented in the README. Network exposure or
  multi-user support requires authentication and stronger concurrency controls.
