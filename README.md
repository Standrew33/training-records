# Evidence školení

Webová aplikace, která z exportů (zaměstnanci, absolvovaná školení, katalog, termíny) ukáže, jak jsou lidé proškoleni, komu končí platnost a koho přihlásit na který termín s ohledem na kapacitu.

Texty k zadání: [ZPRAVA.md](ZPRAVA.md) (co, proč, předpoklady, co chybí) a [AI_ZAZNAM.md](AI_ZAZNAM.md) (použití AI).

## Spuštění

```bash
npm install
npm run dev      # vývoj (nejdřív přegeneruje data, pokud existuje data-raw/)
npm test         # unit testy
npm run build    # produkční build do dist/
```

## Data

- `data-raw/` (gitignorováno) obsahuje čtyři původní CSV. Obsahují rodná čísla, proto se necommitují.
- `npm run prepare-data` je vyčistí (`src/lib/clean.ts`) do `public/data/dataset.json` – bez rodných čísel a poznámek. Tento soubor je commitnutý, takže build funguje i bez surových dat.
- Každá úprava dat je zaznamenána jako „issue“ a zobrazena v záložce **Kvalita dat**.

## Struktura

- `src/lib/parse.ts` – datumy (5 formátů + Excel), ceny, bezpečný CSV export
- `src/lib/clean.ts` – čištění a validace čtyř exportů
- `src/lib/compliance.ts` – stav povinného školení (platné / brzy / prošlé / chybí)
- `src/lib/planner.ts` – doporučení přihlášek s ohledem na kapacitu
- `src/views/` – obrazovky

## Nasazení

GitHub Pages přes `.github/workflows/deploy.yml` (po pushi do `main`). V repozitáři: Settings → Pages → Source: *GitHub Actions*.
