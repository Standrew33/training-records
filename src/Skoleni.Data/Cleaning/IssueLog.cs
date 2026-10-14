using Skoleni.Core.Models;

namespace Skoleni.Data.Cleaning;

/// <summary>
/// Sběrač vad vstupních dat. Pravidlo celého čištění: nic se neopravuje ani nevyřazuje mlčky –
/// každá úprava má záznam „co bylo špatně“ a „co se s tím stalo“.
/// </summary>
public sealed class IssueLog
{
    private readonly List<Issue> issues = [];

    public IReadOnlyList<Issue> Items => issues;

    public void Info(string source, string reference, string description, string resolution) => Add(source, IssueSeverity.Info, reference, description, resolution);
    public void Warn(string source, string reference, string description, string resolution) => Add(source, IssueSeverity.Warning, reference, description, resolution);
    public void Error(string source, string reference, string description, string resolution) => Add(source, IssueSeverity.Error, reference, description, resolution);

    private void Add(string source, IssueSeverity severity, string reference, string description, string resolution) =>
        issues.Add(new Issue(source, severity, reference, description, resolution));
}

public static class Sources
{
    public const string Employees = "zaměstnanci";
    public const string Completions = "absolvovaná školení";
    public const string Catalog = "katalog";
    public const string Terms = "termíny";
}
