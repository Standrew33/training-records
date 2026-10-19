# Training records

A web application over four exports (employees, completed trainings, catalogue, terms): who is trained how well, whose validity is ending, what to resolve, and whom to sign up for which term given limited capacity.

Assignment texts: [REPORT.md](REPORT.md) (what, why, assumptions, what is missing) · [AI_USAGE.md](AI_USAGE.md) (use of AI).

## Architecture

```
data-raw/*.csv ─► Skoleni.DataPrep ─► src/Skoleni.Web/wwwroot/data/dataset.json ─► Skoleni.Web (Blazor WASM)
 (national IDs,      uses                (cleaned, without national IDs               uses Skoleni.Core
  not in git)        Skoleni.Data         and free-text notes; committed)             (status, planner, statistics)
```

| Project | Role |
|---|---|
| `src/Skoleni.Core` | Domain: models, `Compliance` (status of a mandatory training), `Planner` (sign-ups for terms), `Statistics`, `CsvWriter`. No dependencies (runs in the browser). |
| `src/Skoleni.Data` | `CsvTable` (RFC 4180 parser), `CzechDate`/`Money`/`Text`, a cleaner per export and `DatasetBuilder`. Every data change is written to an `IssueLog`. |
| `src/Skoleni.DataPrep` | Console step: raw CSV → `dataset.json`. |
| `src/Skoleni.Web` | Blazor WebAssembly. Presentation only; all logic is in Core. |
| `tests/Skoleni.Tests` | xUnit (109 tests, 4 of them over the real exports): parsers, cleaning of every kind of defect, validity rules, the planner including a property test against brute force, privacy, invariants on the real data. |

## Running

Requires the .NET 10 SDK.

```bash
dotnet test                                       # tests (those over the real exports are skipped when data-raw/ is missing)
dotnet run --project src/Skoleni.DataPrep         # only if you change the exports in data-raw/ (default export date 2026-10-01)
dotnet run --project src/Skoleni.Web              # local server
```

## Sensitive data

- The raw exports contain national ID numbers (rodné číslo) and are in `.gitignore`. The number is read only to find duplicate people and is never stored; the `Employee`, `Completion` and `Term` models have no field for it or for free text (test `Model_types_have_no_member_...`).
- Notes from the source (they contain e.g. formulas like `=HYPERLINK(...)`) are not carried over, and the CSV exports from the app neutralise cells starting with `= + - @`.
- The app runs entirely in the browser and sends no data anywhere.

## Operations and deployment

`.github/workflows/deploy.yml`: `dotnet test` runs on every push and PR; after a push to `main` the Blazor app is published to GitHub Pages (the `<base href>` is rewritten to the repository name). Set Settings → Pages → Source: *GitHub Actions* once in the repository.

New export: copy the CSVs into `data-raw/`, run DataPrep, review the *Data quality* tab, commit the new `dataset.json`.
