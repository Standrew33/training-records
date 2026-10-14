using System.Text;
using Skoleni.Core.Models;
using Skoleni.Data.Cleaning;
using Skoleni.Data.Parsing;

namespace Skoleni.Data;

/// <summary>The four raw exports as text (decoding is up to the caller, see <see cref="TextFile"/>).</summary>
public sealed record RawExports(string Employees, string Completions, string Catalog, string Terms);

public static class DatasetBuilder
{
    /// <summary>
    /// The order follows the dependencies: employees -> catalogue (needs canonical departments and positions)
    /// -> completed trainings (need employees and the catalogue) -> terms (need the catalogue).
    /// </summary>
    public static Dataset Build(RawExports raw, DateOnly exportDate)
    {
        var log = new IssueLog();
        var employees = EmployeeCleaner.Clean(CsvTable.Parse(raw.Employees), exportDate, log);
        var courses = CatalogCleaner.Clean(
            CsvTable.Parse(raw.Catalog),
            employees.Employees.Select(e => e.Department).Distinct().ToList(),
            employees.Employees.Select(e => e.Position).Distinct().ToList(),
            log);
        var completions = CompletionCleaner.Clean(CsvTable.Parse(raw.Completions), employees.Employees, employees.IdAliases, courses, exportDate, log);
        var terms = TermCleaner.Clean(CsvTable.Parse(raw.Terms), courses, log);
        return new Dataset(exportDate, employees.Employees, courses, completions, terms, log.Items);
    }
}

public static class TextFile
{
    /// <summary>Strict UTF-8; when the file is not UTF-8, windows-1250 (that is how absolvovana_skoleni.csv is exported).</summary>
    public static string Read(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = File.ReadAllBytes(path);
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1250).GetString(bytes);
        }
    }
}
