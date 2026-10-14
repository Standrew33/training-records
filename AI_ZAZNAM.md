# Záznam o použití AI

**Nástroj:** Claude Code (model Claude Sonnet 5.5) v aplikaci Claude pro desktop, jedna konverzace.

**Jak jsem AI použil:**
- Zadání a čtyři exportní soubory jsem předal AI. Ta data prošla skripty, popsala jejich vady (kódování, formáty datumů, duplicity, osiřelé záznamy, chybné kódy) a navrhla pravidla čištění.
- AI napsala téměř všechen kód (čištění dat, výpočet stavů školení, plánovač, React obrazovky, testy, README, tuto dokumentaci). Navržený technologický stack (statický TypeScript/React web na GitHub Pages) a pravidla plánování jsem schvaloval já.
- Můj podíl: zadání, výběr technologií (preferuji .NET/TypeScript), kontrola výsledku, rozhodnutí o předpokladech a nasazení.

**Co AI zkusila a muselo se opravit:**
- Instalace nejnovějších verzí Vite/Vitest selhala (chybějící nativní binárky); AI přešla na stabilní Vite 5 / Vitest 2 a projekt přesunula z dočasné složky aplikace na běžný disk.
- První verze opravy kódů školení by tiše mapovala `BOZP-X` na `BOZP-Z`; AI to zpřísnila tak, aby se opravovala jen záměna písmene za číslici (`KYB3R`), ostatní se hlásí jako chyba.
- Zkrácený útvar „Studijní“ se nejprve nesloučil se „Studijní oddělení“; zjištěno při kontrole výstupu v prohlížeči a opraveno.
- Označení „pozdě“ u plánu se původně zobrazovalo u všech už prošlých školení; omezeno na případy, kdy termín nestihne teprve končící platnost.

**Co jsem ověřoval:** build a typová kontrola prochází, 6 unit testů, ruční prohlídka všech pěti záložek v prohlížeči (bez chyb v konzoli), kontrola, že výstupní JSON neobsahuje rodná čísla. Plánovač jsem nechal spustit i mimo UI skriptem nad stejným kódem (počty a kapacity sedí s UI); nezávislé přepočítání ručně ani jiným nástrojem jsem neprovedl.

**Rizika:** výsledky stojí na předpokladech ve `ZPRAVA.md`; nesrovnalosti v datech nejsou ověřené s vlastníkem dat.
