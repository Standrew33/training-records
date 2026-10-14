using Skoleni.Core.Models;

namespace Skoleni.Data.Cleaning;

/// <summary>
/// Collector of input-data defects. The rule of the whole cleaning step: nothing is silently corrected or dropped -
/// every change has a record of "what was wrong" and "what was done about it".
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
    public const string Employees = "employees";
    public const string Completions = "completed trainings";
    public const string Catalog = "catalogue";
    public const string Terms = "terms";
}
