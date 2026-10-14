# Záznam o použití AI

**Nástroj:** Claude Code (Claude Sonnet 5.5) v desktopové aplikaci Claude, jedna konverzace. Zadání a čtyři exporty jsem AI předal; veškerý kód, testy a dokumentaci vygenerovala AI, já zadání upřesňoval a výstupy schvaloval.

**Průběh a rozhodnutí:**
- Nejdřív AI zprofilovala data (kódování, formáty datumů, duplicity, osiřelé záznamy, chybné kódy) a postavila první prototyp v TypeScriptu/Reactu. To byla chyba v zadání technologie: já se specializuji na .NET/C#, TypeScript znám málo a nedokážu takový kód ověřit ani dál provozovat. Prototyp jsem zavrhl a AI vše přepsala do C# (jádro, čištění, testy, Blazor WebAssembly); TypeScript v repozitáři nezůstal.
- AI původně zvolila .NET 9, protože to byl nejnovější nainstalovaný SDK, aniž ověřila novější verzi; na mou výtku byl nainstalován .NET SDK 10 a projekt převeden na net10.0.
- Po přechodu na .NET 10 aplikace v prohlížeči přestala načítat data (synchronní čtení proudu z HttpClient ve WASM už nefunguje). Testy to nezachytily, odhalila to až ruční kontrola v prohlížeči; opraveno na asynchronní čtení.
- Z prototypu zůstaly jen pravidla čištění a plánování a jeho čísla; ta posloužila jako kontrola nové implementace (shoda: 751 povinností, 77 problémových, 201 přihlášek).

**Co AI zkazila a opravilo se (příklady):**
- Oprava kódů školení původně tiše mapovala `BOZP-X` na `BOZP-Z`; zpřísněno na jednoznačnou záměnu písmene za číslici.
- Zkrácený útvar „Studijní“ se nesloučil se „Studijní oddělení“ (zjištěno při prohlídce výstupu v prohlížeči).
- Plánovač nejdřív „zabral“ člověku jediný termín a druhé školení v kolizi zůstalo bez termínu (zachytil test); řazení doplněno o počet dostupných termínů.
- Značka „pozdě“ se zpočátku ukazovala u všech už prošlých školení; zúžena na případ, kdy přidělený termín propadne teprve končící platnost.
- Instalace nejnovějšího Vite/Vitestu v prototypu selhala; irelevantní po přechodu na .NET.

**Co jsem ověřoval a co ne:** 109 automatických testů (z toho 4 nad skutečnými exporty; ty se bez data-raw/ přeskočí, takže v CI běží 105), kontrola aplikace v prohlížeči na všech záložkách, shoda čísel s prototypem a kontrola, že výstupní JSON neobsahuje rodná čísla. Neověřil jsem předpoklady v `ZPRAVA.md` s vlastníkem dat a nezkoumal jsem nezávisle správnost čísel mimo kód AI.

**Citlivá data:** rodná čísla se v kódu čtou jen k odhalení duplicitních osob a nikam se neukládají; do konverzace s AI se dostala jen jako součást exportů, které jsou fiktivní.
