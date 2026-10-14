# AI usage log

**Tool:** Claude Code (Claude Sonnet 5.5) in the Claude desktop app, a single conversation. I gave the AI the assignment and the four exports; the AI generated essentially all code, tests and documentation, while I refined the assignment and reviewed the outputs.

**Course of work and decisions:**
- The AI first profiled the data (encoding, date formats, duplicates, orphan records, wrong codes) and built a first prototype in TypeScript/React. That was a mistake with respect to my brief: my specialty is .NET/C#, so the prototype should not have been TypeScript-first. I rejected it and the AI rewrote everything in C# (core, cleaning, tests, Blazor WebAssembly).
- Only the cleaning and planning rules and the prototype's figures survived; the figures served as a cross-check of the new implementation (agreement: 751 obligations, 77 problems, 201 sign-ups).
- The AI first chose .NET 9 because it was the newest SDK installed, without checking for a newer one; at my request .NET SDK 10 was installed and the project moved to net10.0.
- At my request all code, comments, UI text, data-issue messages and documentation were then translated to English (input data values stay Czech, as they are data).

**What the AI got wrong and was corrected (examples):**
- Course-code repair originally mapped `BOZP-X` to `BOZP-Z` silently; it was tightened to an unambiguous letter-for-digit swap only.
- A shortened department "Studijní" was not merged with "Studijní oddělení" (found by inspecting the output in a browser).
- The planner first "used up" the only term for one course and left the same person's second course, which collided, without a term (caught by a test); the ordering now also considers how many terms are available.
- The "late" flag first showed on every already-expired training; it was narrowed to the case where the assigned term lets a still-valid training lapse.
- After moving to .NET 10 the app stopped loading data in the browser (synchronous stream reads from HttpClient no longer work in WASM). The tests did not catch it; only manual checking in the browser did. Fixed with asynchronous reading.
- In the Razor views the term time was rendered literally as "HH:mm" (a format specifier does not work in Razor markup); the tests did not cover it, the browser check did. Fixed with a formatting helper.

**What I verified and what not:** 109 automated tests (4 of them over the real exports; they are skipped without `data-raw/`, so CI runs 105), a check of the app in the browser on all tabs, agreement of the figures with the prototype and a check that the output JSON contains no national ID numbers. I did not confirm the assumptions in `REPORT.md` with the data owner and did not independently verify the correctness of the numbers outside the AI's code. The GitHub Actions deployment was not verified before the push.

**Sensitive data:** national ID numbers are read in code only to detect duplicate people and are never stored; they reached the AI conversation only as part of the exports, which are fictitious.
