# Markdown Game Tracker

An ASP.NET Core backend that treats the Markdown game notes in an Obsidian vault as its data store. The filename is the game title/ID, YAML frontmatter holds structured fields, and the rest of the file remains ordinary Markdown for notes, thoughts, progress logs, and reviews.

## Frontend

The root page at [`http://localhost:5297/`](http://localhost:5297/) is a responsive Razor Pages dashboard with a pastel-blue theme. It provides:

- Five status tabs with counts for Active, Endless, Completed, Inactive, and Plan to play
- Search scoped to the selected status tab
- Game cards with status, rating, and note excerpts
- Clickable status badges for moving a game between statuses directly from its card
- A day/night theme toggle that follows the device preference and remembers your choice
- Game details and frontmatter display
- An IGDB-powered game description on each details page, with a match picker for similarly named games
- IGDB cover art, wide artwork, and a screenshot gallery for the selected game match
- Formatted Markdown viewing with headings, lists, tables, task lists, links, quotes, and code blocks
- A split Write/Preview editor with formatting controls and keyboard shortcuts
- Create and edit forms that write through the same Markdown repository as the API
- Confirmed deletion of game notes
- An **API docs** navigation link to Swagger in development

The app homepage opens automatically when using either local launch profile.

## Current API

In development, interactive Swagger documentation is available at [`/swagger`](http://localhost:5297/swagger), backed by the OpenAPI 3.1 document at [`/openapi/v1.json`](http://localhost:5297/openapi/v1.json). Both are intentionally disabled outside the Development environment to avoid disclosing API details in production.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/games?status=active&search=term` | List, filter, and search games |
| `GET` | `/api/games/{id}` | Read one game by filename without `.md` |
| `GET` | `/api/games/{id}/igdb-description` | Fetch a read-only game description from IGDB |
| `GET` | `/api/games/{id}/igdb-matches` | Find likely IGDB titles and release years |
| `POST` | `/api/games` | Create a game note |
| `PUT` | `/api/games/{id}` | Update fields or rename a game note |
| `DELETE` | `/api/games/{id}` | Permanently delete a game note |
| `POST` | `/api/markdown/preview` | Render safe HTML for the live Markdown preview |

Only top-level Markdown files whose frontmatter contains `type: game` are exposed. This prevents the API from treating `Games/Games.md`, which is an index note, as a game. All paths are confined to the configured games directory.

Ratings are optional numbers from 0 through 10. Every new game uses one of five canonical status values: `active`, `endless`, `completed`, `inactive`, or `planned`. Use `endless` for ongoing games without a meaningful completion point, while `active` is for games whose progress or ending you are tracking. The UI displays `planned` as **Plan to play**. Legacy values such as `plan to play`, `planning`, and `backlog` are read as `planned`.

## Run locally

Set the `Vault__Path` environment variable to point to your Obsidian vault, then start the service:

```powershell
$env:Vault__Path = "C:\path\to\your\vault"
dotnet run --project .\MarkdownGameTracker\MarkdownGameTracker.csproj
```

`Vault__GamesDirectory` defaults to `Games` and must resolve inside the vault.

For local development, .NET user secrets keep the vault path and IGDB credentials out of tracked files:

```powershell
dotnet user-secrets --project MarkdownGameTracker set "Vault:Path" "C:\path\to\your\vault"
dotnet user-secrets --project MarkdownGameTracker set "IGDB:ClientId" "your-client-id"
dotnet user-secrets --project MarkdownGameTracker set "IGDB:ClientSecret" "your-client-secret"
```

Environment variables remain the preferred option for Docker and deployed environments.

### IGDB descriptions

Create a Twitch developer application, then provide its client ID and client secret. The app exchanges those credentials for an app access token on the server; the secret is never sent to the browser.

For local development, set these environment variables before starting the app:

```powershell
$env:IGDB__ClientId = "your-client-id"
$env:IGDB__ClientSecret = "your-client-secret"
```

Opening a game's details page then loads its IGDB description, cover, wide artwork, and up to six screenshots asynchronously. If the automatic match is wrong, **Choose another match** reveals up to ten likely titles with their release years and cover thumbnails. A manual selection is remembered in that browser's local storage, while results are cached in server memory. Neither the description, media URLs, nor any IGDB identifier is added to the Markdown file. Without credentials, the rest of the details page continues to work and shows a setup message in the description panel.

## Docker

Docker needs both pieces of configuration:

- A **bind mount** exposes the host's Obsidian vault at `/vault` inside the container.
- `Vault__Path=/vault` tells the application where to find that mounted directory.

The included `compose.yaml` wires the container side automatically. Copy the example environment file and set the host path:

```powershell
Copy-Item .env.example .env
```

On Windows with Docker Desktop, `.env` can use forward slashes:

```dotenv
OBSIDIAN_VAULT_PATH=C:/Users/you/path/to/Obsidian/Vault
GAME_TRACKER_PORT=5297
IGDB_CLIENT_ID=your-client-id
IGDB_CLIENT_SECRET=your-client-secret
```

Then build and start the app:

```powershell
docker compose up --build
```

Open [`http://localhost:5297`](http://localhost:5297). The bind mount is read/write because the CRUD interface needs to create and update Markdown files. The local `.env` file is ignored by Git.

The equivalent direct Docker commands are:

```powershell
docker build -t markdown-game-tracker -f .\MarkdownGameTracker\Dockerfile .
docker run --rm -p 5297:8080 -e Vault__Path=/vault --mount type=bind,source="C:\path\to\vault",target=/vault markdown-game-tracker
```

Stop the Compose app with:

```powershell
docker compose down
```

## Request shape

Create a note:

```json
{
  "title": "Hades II",
  "status": "planned",
  "rating": 8.5,
  "markdown": "## Thoughts\nReady for another run.",
  "frontmatter": {
    "platforms": ["PC", "Steam Deck"]
  }
}
```

Updates are merge-based: omitted values preserve the existing note, changing `title` renames the file, and extra frontmatter fields are retained. To remove a frontmatter field, send that key with a `null` value inside `frontmatter`.

New notes automatically receive the vault's existing defaults:

```yaml
type: game
hobby:
  - "[[Gaming]]"
```

Writes use a temporary file in the same directory before replacing the destination, reducing the chance of leaving a partially written note. Rewriting a note preserves frontmatter values and Markdown content, but YAML whitespace, quoting, and comments may be normalized.

Markdown rendering uses the same server-side pipeline for saved-note pages and live editor previews. Raw HTML is displayed as text rather than executed. The editor supports Write, Split, and Preview layouts plus shortcuts for bold (`Ctrl+B`), italic (`Ctrl+I`), and links (`Ctrl+K`).

## Verification

The test suite uses a temporary vault and never writes to the real Obsidian vault:

```powershell
dotnet test .\MarkdownGameTracker.slnx
```

This first version has no authentication and should remain local-only. Add authentication and stronger concurrency controls before exposing it on a network or building a multi-user client.
