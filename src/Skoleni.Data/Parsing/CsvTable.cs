using System.Text;

namespace Skoleni.Data.Parsing;

/// <summary>Jeden řádek CSV s hlavičkou. Chybějící sloupec = prázdný řetězec (exporty mají nestejnou šířku řádků).</summary>
public sealed class CsvRow(int line, IReadOnlyDictionary<string, string> values)
{
    /// <summary>Číslo řádku v souboru (1 = hlavička) – pro dohledatelnost v hlášení o vadách.</summary>
    public int Line { get; } = line;

    public string this[string column] => values.TryGetValue(column, out var v) ? v : "";

    public string Trimmed(string column) => this[column].Trim();
}

/// <summary>
/// Minimální RFC 4180 parser: uvozovky, zdvojené uvozovky, nový řádek uvnitř uvozovek.
/// Oddělovač se určuje z hlavičky (zaměstnanci.csv má čárku, ostatní středník).
/// Řádky bez dat se přeskočí; delší řádky se oříznou na hlavičku, kratší se doplní prázdnem.
/// </summary>
public static class CsvTable
{
    public static IReadOnlyList<CsvRow> Parse(string text)
    {
        text = text.TrimStart('﻿');
        var delimiter = DetectDelimiter(text);
        var records = ReadRecords(text, delimiter);
        if (records.Count == 0) return [];

        var header = records[0].Fields.Select(h => h.Trim()).ToArray();
        var rows = new List<CsvRow>();
        foreach (var (line, fields) in records.Skip(1))
        {
            if (fields.All(string.IsNullOrWhiteSpace)) continue;
            var dict = new Dictionary<string, string>();
            for (var i = 0; i < header.Length; i++)
                dict[header[i]] = i < fields.Count ? fields[i] : "";
            rows.Add(new CsvRow(line, dict));
        }
        return rows;
    }

    private static char DetectDelimiter(string text)
    {
        var firstLine = text.Split('\n', 2)[0];
        return firstLine.Count(c => c == ';') > firstLine.Count(c => c == ',') ? ';' : ',';
    }

    private static List<(int Line, List<string> Fields)> ReadRecords(string text, char delimiter)
    {
        var records = new List<(int, List<string>)>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var recordStartLine = 1;

        void EndField() { fields.Add(field.ToString()); field.Clear(); }
        void EndRecord()
        {
            EndField();
            records.Add((recordStartLine, fields));
            fields = [];
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else
                {
                    if (c == '\n') line++;
                    field.Append(c);
                }
                continue;
            }
            switch (c)
            {
                case '"' when field.Length == 0: inQuotes = true; break;
                case var d when d == delimiter: EndField(); break;
                case '\r': break;
                case '\n':
                    EndRecord();
                    line++;
                    recordStartLine = line;
                    break;
                default: field.Append(c); break;
            }
        }
        if (field.Length > 0 || fields.Count > 0) EndRecord();
        return records;
    }
}
