# CLAUDE.md

This file guides Claude when working in this repository.

## What this is

Markdown Game Tracker is an ASP.NET Core (.NET 10) app. It uses the Markdown game notes in an Obsidian vault as its only data store. It has Razor Pages for the UI and minimal-API endpoints under `/api`. There is no database. Each game is one `.md` file in `<Vault:Path>/<Vault:GamesDirectory>` (default `Games`), with YAML frontmatter followed by free-form Markdown.

The notes are the user's real Obsidian vault. Preserving their data is the top priority.

## Commands

```powershell
dotnet build .\MarkdownGameTracker.slnx
dotnet test  .\MarkdownGameTracker.slnx                       # xUnit, uses a temp vault
dotnet test  .\MarkdownGameTracker.slnx --filter "FullyQualifiedName~ProgressJournalTests"   # single class
dotnet run --project .\MarkdownGameTracker\MarkdownGameTracker.csproj   # http://localhost:5297, Swagger at /swagger (Development only)

# Playwright browser suite (Node 20+). Run it after dotnet test finishes, since both use the same build output
cd browser-tests; npm install; npx playwright install chromium; npm test
# $env:BROWSER_CHANNEL = "msedge" uses installed Edge instead of Chromium

docker compose up --build    # needs .env (copy .env.example); bind-mounts the vault at /vault
```

Local config comes from user secrets (`Vault:Path`, `IGDB:ClientId`, `IGDB:ClientSecret`) or the environment variables `Vault__Path` and `IGDB__ClientId`/`IGDB__ClientSecret`. The app fails at startup if `Vault:Path` is empty. IGDB is optional, and the app works without it.

## Layout

- `MarkdownGameTracker/Program.cs`: DI wiring and the pipeline. OpenAPI and Swagger are mapped only in Development.
- `Api/`: minimal-API endpoint groups (`GameEndpoints`, `GameMetadataEndpoints` for IGDB, `MarkdownEndpoints` for the preview renderer).
- `Storage/`: the core of the app.
  - `MarkdownGameRepository` (`IGameRepository`) handles all file I/O. Paths are confined to the games directory. Writes go to a temp file in the same directory and then replace the original.
  - `GameNoteDocumentSerializer` splits and joins frontmatter and Markdown with YamlDotNet. It normalizes `JsonElement` and YAML values into plain CLR types.
  - `GameNoteUpdatePolicy` applies merge-based updates. It handles rating validation and clearing, `null` meaning "remove key", and `type`/`title`/`status` being controlled only by the typed request.
  - `GameNoteFileNames` produces Windows-safe filenames on every OS and handles case-only renames.
  - `ProgressJournalDocument` finds, reads, and replaces the `## Progress Journal` section. It checks a SHA-256 version for conflicts and validates that the section's boundaries are unchanged after an edit.
- `Models/`: request/response records and `GameStatuses` (canonical statuses plus legacy aliases).
- `Services/`: IGDB (token provider, API client, matching, mapping, description service with a memory cache), the HowLongToBeat CSV import, and `MarkdownRenderer` (Markdig, raw HTML escaped).
- `Pages/`: Razor Pages (`Index` dashboard, `Games/Create|Edit|Details|Import`, shared inline status/rating controls).
- `wwwroot/js`, `wwwroot/css`: plain JS and CSS with no bundler. `wwwroot/lib` is vendored Bootstrap and jQuery, so don't edit it.
- `MarkdownGameTracker.Tests/`: xUnit integration tests via `WebApplicationFactory<Program>`. `TestSupport.cs` has `TestApp`, which creates a temp vault and swaps IGDB for a fake.
- `browser-tests/`: Playwright (`node --test`). It starts its own server and temp vault, and IGDB responses are mocked.

## Domain rules to preserve

- A file is a game only if its frontmatter has `type: game` and it sits at the top level of the games directory. `Games/Games.md` is an index note and must stay excluded.
- The note ID is the filename without `.md`. The display title is the frontmatter `title`, falling back to the filename. Changing the title renames the file.
- Statuses are `active`, `endless`, `completed`, `inactive`, `planned` (shown as "Plan to play"). Legacy values `plan to play`, `planning`, `backlog` read as `planned`.
- Rating is an optional decimal from 0 to 10. A typed `rating` takes precedence. `clearRating: true` removes it, and setting and clearing a rating in the same request is an error. Ratings set through frontmatter get the same validation.
- New notes get the defaults `type: game`, `hobby: ["[[Gaming]]"]`, and `title`.
- Unknown frontmatter keys must survive every round-trip. Rewrites may normalize YAML whitespace and quoting, but must not lose values or Markdown content.
- A malformed note must not break the library. It is reported as a diagnostic (`/api/games/scan`, homepage) and is never overwritten or deleted by the app.
- Never write IGDB data (descriptions, media URLs, IDs) into notes. Manual IGDB match choices live only in browser localStorage, keyed by note ID.
- The HLTB import is preview-first and never overwrites existing notes.
- There is no authentication, so the app is local-only.

## Working conventions

- Tests must never touch the real vault. Use `TestApp` and its temp `RootPath`.
- Add or adjust tests in the matching `*Tests.cs` file when changing storage or update semantics. `DataPreservationTests` and `ProgressJournalTests` cover the riskiest paths.
- Keep the README's API table and behavior notes in sync with endpoint changes.
- Don't read, print, or commit `.env` or user secrets. The large `markdown-game-tracker-*.tar` image export and `.tmp-rating-vault/` in the repo root are local artifacts, not source.
