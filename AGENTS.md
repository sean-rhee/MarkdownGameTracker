
# Project Instructions

## Repository
This is a C#/.NET application.

Before changing code, inspect the relevant
implementation and identify existing patterns.
Prefer those patterns over introducing new ones.

## Architecture
- Respect existing component boundaries.
- Keep business logic separate from presentation
  and HTTP handling where practical.
- Use existing dependency injection conventions.
- Avoid unnecessary abstractions or dependencies.

## Changes
- Keep changes focused on the requested task.
- Avoid unrelated refactoring.
- Explain significant architectural trade-offs.
- Ask before adding production dependencies
  or introducing breaking changes.

## Verification
- Identify the relevant build and test commands.
- Verify changes with appropriate checks.
- Fix failures introduced by your changes.
- Do not claim success if checks were not run.
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

## Development
- Build: `dotnet build MarkdownGameTracker.sln`
- Test: `dotnet test MarkdownGameTracker.sln`