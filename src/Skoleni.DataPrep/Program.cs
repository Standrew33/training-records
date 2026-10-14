using Skoleni.Core.Models;
using Skoleni.Data;

// Použití: dotnet run --project src/Skoleni.DataPrep -- [složka-s-CSV] [výstupní-json] [datum-exportu]
// Výchozí: data-raw/  →  src/Skoleni.Web/wwwroot/data/dataset.json, datum exportu 2026-10-01.
// Surová data obsahují rodná čísla; do výstupu se nedostanou (viz EmployeeCleaner a model Employee).

var inputDir = args.ElementAtOrDefault(0) ?? "data-raw";
var output = args.ElementAtOrDefault(1) ?? Path.Combine("src", "Skoleni.Web", "wwwroot", "data", "dataset.json");
var exportDate = DateOnly.Parse(args.ElementAtOrDefault(2) ?? "2026-10-01");

string[] required = ["zamestnanci.csv", "absolvovana_skoleni.csv", "katalog_skoleni.csv", "terminy_skoleni.csv"];
var missing = required.Where(f => !File.Exists(Path.Combine(inputDir, f))).ToList();
if (missing.Count > 0)
{
    Console.Error.WriteLine($"V '{inputDir}' chybí: {string.Join(", ", missing)}");
    return 1;
}

var ds = DatasetBuilder.Build(new RawExports(
    Employees: TextFile.Read(Path.Combine(inputDir, "zamestnanci.csv")),
    Completions: TextFile.Read(Path.Combine(inputDir, "absolvovana_skoleni.csv")),
    Catalog: TextFile.Read(Path.Combine(inputDir, "katalog_skoleni.csv")),
    Terms: TextFile.Read(Path.Combine(inputDir, "terminy_skoleni.csv"))), exportDate);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, DatasetJsonContext.Serialize(ds));

int Count(IssueSeverity s) => ds.Issues.Count(i => i.Severity == s);
Console.WriteLine($"Zaměstnanců {ds.Employees.Count}, absolvovaných školení {ds.Completions.Count}, termínů {ds.Terms.Count} ({ds.Terms.Count(t => t.Usable)} použitelných).");
Console.WriteLine($"Vady dat: {Count(IssueSeverity.Error)} chyb, {Count(IssueSeverity.Warning)} varování, {Count(IssueSeverity.Info)} informací.");
Console.WriteLine($"Zapsáno: {output}");
return 0;
