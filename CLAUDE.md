Project agent instructions live in `AGENTS.md`, shared with other tools and imported below so Claude Code loads the same rules from one source of truth. Domain vocabulary is in `CONTEXT.md`.

@AGENTS.md
@CONTEXT.md

## Claude Code notes

- Use the .NET 11 SDK pinned in `global.json`. If it is missing locally, install it with the official `dotnet-install` script into `~/.dotnet`.
- Build and test with `dotnet test` from the repo root. Warnings are errors.
