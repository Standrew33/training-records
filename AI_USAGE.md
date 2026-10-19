# Use of AI

I used Claude Code (Claude Sonnet 5.5) as a coding assistant throughout this task: profiling the exports, drafting the C# code and tests, drafting the documentation, and running the tests and the app in a browser.

The decisions were mine: the stack (.NET 10, Blazor WebAssembly, free static hosting on GitHub Pages), the scope and priorities, the data-handling rules and assumptions listed in `REPORT.md`, and which AI output to keep or change. The assistant's results were checked by the automated tests (`dotnet test`) and by opening the deployed app.

Limits: the assumptions in `REPORT.md` were not confirmed with the data owner, and the AI-drafted code has not had an independent review.
