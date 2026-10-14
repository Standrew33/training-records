using System.Text;
using Skoleni.Core.Models;

namespace Skoleni.Core.Domain;

public sealed record StatusCounts(int Valid, int ExpiringSoon, int Expired, int Missing)
{
    public int Total => Valid + ExpiringSoon + Expired + Missing;
    public int Problems => Expired + Missing;

    /// <summary>Podíl povinností, které nejsou prošlé ani chybějící (tj. platné + brzy vyprší).</summary>
    public double OkShare => Total == 0 ? 1 : (double)(Valid + ExpiringSoon) / Total;

    public static StatusCounts Of(IEnumerable<Requirement> reqs)
    {
        var l = reqs.ToList();
        int N(TrainingStatus s) => l.Count(r => r.Status == s);
        return new StatusCounts(N(TrainingStatus.Valid), N(TrainingStatus.ExpiringSoon), N(TrainingStatus.Expired), N(TrainingStatus.Missing));
    }
}

public static class Statistics
{
    /// <summary>
    /// Co se počítá do přehledů: aktivní a nastupující. Rodičovská se nezapočítává (nikdo po ní nechce školení,
    /// dokud je mimo práci), ukončení jsou už vyřazeni v <see cref="Compliance.Compute"/>.
    /// </summary>
    public static IEnumerable<Requirement> Counted(IEnumerable<Requirement> reqs, DateOnly asOf) =>
        reqs.Where(r => Compliance.GroupOf(r.Employee, asOf) is EmployeeGroup.Active or EmployeeGroup.Starting);
}

/// <summary>Zápis CSV pro Excel (středník, UTF-8 s BOM) odolný proti vložení vzorců (CSV/Excel injection).</summary>
public static class CsvWriter
{
    public static string Escape(string? value)
    {
        var s = value ?? "";
        if (s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r') s = "'" + s;
        return s.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }

    public static string Write(IEnumerable<string> header, IEnumerable<IEnumerable<string?>> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(';', header.Select(Escape))).Append("\r\n");
        foreach (var row in rows) sb.Append(string.Join(';', row.Select(Escape))).Append("\r\n");
        return sb.ToString();
    }

    public static string ToDataUri(string csv) =>
        "data:text/csv;charset=utf-8;base64," + Convert.ToBase64String(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray());
}
