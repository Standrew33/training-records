# Evidence školení

Webová aplikace nad čtyřmi exporty (zaměstnanci, absolvovaná školení, katalog, termíny): kdo je jak proškolen, komu končí platnost, co řešit a koho přihlásit na který termín s ohledem na kapacitu.

Texty k zadání: [ZPRAVA.md](ZPRAVA.md) (co, proč, předpoklady, co chybí) · [AI_ZAZNAM.md](AI_ZAZNAM.md) (použití AI).

## Architektura

```
data-raw/*.csv ─► Skoleni.DataPrep ─► src/Skoleni.Web/wwwroot/data/dataset.json ─► Skoleni.Web (Blazor WASM)
 (rodná čísla,       používá            (vyčištěno, bez rodných čísel               používá Skoleni.Core
  není v gitu)       Skoleni.Data        a volných poznámek; je v gitu)             (stavy, plánovač, statistiky)
```

| Projekt | Role |
|---|---|
| `src/Skoleni.Core` | Doména: modely, `Compliance` (stav povinného školení), `Planner` (přihlášení na termíny), `Statistics`, `CsvWriter`. Bez závislostí (běží v prohlížeči). |
| `src/Skoleni.Data` | `CsvTable` (RFC 4180 parser), `CzechDate`/`Money`/`Text`, čističe jednotlivých exportů a `DatasetBuilder`. Každá úprava dat se zapíše do `IssueLog`. |
| `src/Skoleni.DataPrep` | Konzolový krok: surové CSV → `dataset.json`. |
| `src/Skoleni.Web` | Blazor WebAssembly. Jen zobrazení; veškerá logika je v Core. |
| `tests/Skoleni.Tests` | xUnit (113 testů): parsery, čištění každého druhu vady, pravidla platnosti, plánovač včetně property-testu proti hrubé síle, ochrana soukromí, kontroly invariantů na skutečných datech. |

## Spuštění

Potřeba je .NET SDK 9.

```bash
dotnet test                                       # testy (testy nad skutečnými exporty se přeskočí, pokud chybí data-raw/)
dotnet run --project src/Skoleni.DataPrep         # jen pokud změníte exporty v data-raw/ (výchozí datum exportu 2026-10-01)
dotnet run --project src/Skoleni.Web              # lokální server
```

## Citlivá data

- Surové exporty obsahují rodná čísla a jsou v `.gitignore`. Rodné číslo se čte jen k nalezení duplicitních osob a nikam se neukládá; modely `Employee`, `Completion` ani `Term` pro ně ani pro volný text nemají pole (test `Model_types_have_no_member_...`).
- Poznámky ze zdroje (obsahují např. vzorce typu `=HYPERLINK(...)`) se nepřebírají a CSV exporty z aplikace buňky začínající `= + - @` neutralizují.
- Aplikace běží celá v prohlížeči; žádná data neodesílá.

## Provoz a nasazení

`.github/workflows/deploy.yml`: při každém pushi a PR běží `dotnet test`; po pushi do `main` se Blazor aplikace publikuje na GitHub Pages (přepíše `<base href>` na název repozitáře). V repozitáři je potřeba jednou nastavit Settings → Pages → Source: *GitHub Actions*.

Nový export: zkopírovat CSV do `data-raw/`, spustit DataPrep, zkontrolovat záložku *Kvalita dat*, commitnout nový `dataset.json`.
