using Skoleni.Core.Domain;
using Skoleni.Core.Models;
using Skoleni.Data;
using Xunit.Abstractions;

namespace Skoleni.Tests;

/// <summary>Runs only when a real export is available in data-raw/ (it is not in git - it contains national ID numbers).</summary>
public sealed class FactIfRawDataAttribute : FactAttribute
{
    public FactIfRawDataAttribute()
    {
        if (RealData.Dir is null) Skip = "data-raw/ is not available";
    }
}

internal static class RealData
{
    public static string? Dir { get; } = FindDir();

    private static string? FindDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "data-raw");
            if (File.Exists(Path.Combine(candidate, "zamestnanci.csv"))) return candidate;
        }
        return null;
    }

    public static readonly Lazy<Dataset> Dataset = new(() => DatasetBuilder.Build(new RawExports(
        TextFile.Read(Path.Combine(Dir!, "zamestnanci.csv")),
        TextFile.Read(Path.Combine(Dir!, "absolvovana_skoleni.csv")),
        TextFile.Read(Path.Combine(Dir!, "katalog_skoleni.csv")),
        TextFile.Read(Path.Combine(Dir!, "terminy_skoleni.csv"))), new DateOnly(2026, 10, 1)));
}

/// <summary>
/// Checks on the real exports. These are not "golden" values computed by the program itself,
/// but invariants that must hold whatever the data contains.
/// </summary>
public class RealDataTests(ITestOutputHelper output)
{
    private static readonly DateOnly AsOf = new(2026, 10, 1);

    [FactIfRawData]
    public void Referential_integrity_after_cleaning()
    {
        var ds = RealData.Dataset.Value;
        var emps = ds.Employees.Select(e => e.Id).ToHashSet();
        var codes = ds.Courses.Select(c => c.Code).ToHashSet();
        Assert.Equal(ds.Employees.Count, emps.Count);                                  // employee IDs are unique
        Assert.All(ds.Completions, c => { Assert.Contains(c.EmployeeId, emps); Assert.Contains(c.CourseCode, codes); Assert.True(c.Date <= ds.ExportDate); });
        Assert.Equal(ds.Completions.Count, ds.Completions.Select(c => c.Id).Distinct().Count());
        Assert.All(ds.Terms.Where(t => t.Usable), t => { Assert.Contains(t.CourseCode, codes); Assert.True(t.Capacity > 0); });
        Assert.DoesNotContain(ds.Employees, e => string.IsNullOrWhiteSpace(e.Department));
    }

    [FactIfRawData]
    public void Output_json_contains_no_identity_number_pattern()
    {
        var json = DatasetJsonContext.Serialize(RealData.Dataset.Value);
        Assert.DoesNotMatch(@"\d{6}/\d{3,4}", json);
    }

    [FactIfRawData]
    public void Every_requirement_is_classified_and_plan_respects_all_hard_constraints()
    {
        var ds = RealData.Dataset.Value;
        foreach (var soon in new[] { 30, 90, 180 })
        {
            var reqs = Compliance.Compute(ds, AsOf, soon);
            Assert.Equal(reqs.Count, Enum.GetValues<TrainingStatus>().Sum(s => reqs.Count(r => r.Status == s)));
            Assert.Equal(reqs.Count, reqs.Select(r => (r.Employee.Id, r.Course.Code)).Distinct().Count()); // no obligation twice

            var plan = Planner.Build(ds, reqs, AsOf);
            foreach (var term in ds.Terms)
                Assert.True(plan.ForTerm(term.Id).Count() <= term.FreeSeats, $"{term.Id} capacity exceeded");
            Assert.All(plan.Assignments, a =>
            {
                Assert.True(a.Term.Usable && a.Term.Date >= AsOf);
                Assert.Equal(a.Requirement.Course.Code, a.Term.CourseCode);
                Assert.NotEqual(EmployeeGroup.ParentalLeave, Compliance.GroupOf(a.Requirement.Employee, AsOf));
                if (a.Requirement.Employee.TerminationDate is { } end) Assert.True(a.Term.Date <= end);
            });
            Assert.Equal(plan.Assignments.Count, plan.Assignments.Select(a => (a.Requirement.Employee.Id, a.Requirement.Course.Code)).Distinct().Count());
            Assert.Equal(reqs.Count(r => r.NeedsAction && Compliance.GroupOf(r.Employee, AsOf) is EmployeeGroup.Active or EmployeeGroup.Starting),
                plan.Assignments.Count + plan.Unassigned.Count);          // nobody gets lost
        }
    }

    [FactIfRawData]
    public void Prints_headline_numbers_for_the_report()
    {
        var ds = RealData.Dataset.Value;
        var reqs = Compliance.Compute(ds, AsOf, 90);
        var plan = Planner.Build(ds, reqs, AsOf);
        output.WriteLine($"employees {ds.Employees.Count}; records {ds.Completions.Count}; usable terms {ds.Terms.Count(t => t.Usable)}/{ds.Terms.Count}");
        foreach (var g in reqs.GroupBy(r => r.Status)) output.WriteLine($"{g.Key}: {g.Count()}");
        output.WriteLine($"plan: {plan.Assignments.Count} sign-ups, {plan.Unassigned.Count} without a term, {plan.Assignments.Count(a => a.WillLapse)} will lapse, cost {plan.EstimatedCost:N0}");
        // Why they lapse: is the nearest term at all after the deadline (calendar), or would one exist but is full (capacity)?
        var lapses = plan.Assignments.Where(a => a.WillLapse).ToList();
        bool NoTermBeforeDeadline(Assignment a) => !ds.Terms.Any(t => t.Usable && t.CourseCode == a.Requirement.Course.Code && t.Date >= AsOf && t.Date <= a.Requirement.Deadline && t.FreeSeats > 0);
        output.WriteLine($"lapsing: {lapses.Count}, of which because of the calendar (no term before the deadline): {lapses.Count(NoTermBeforeDeadline)}, because of capacity: {lapses.Count(a => !NoTermBeforeDeadline(a))}");
        foreach (var g in lapses.GroupBy(a => a.Requirement.Course.Code)) output.WriteLine($"  {g.Key}: {g.Count()}, earliest deadline {g.Min(a => a.Requirement.Deadline)}");
        foreach (var c in plan.Coverage) output.WriteLine($"{c.CourseCode}: needed {c.Needed}, free {c.FreeSeats}, uncovered {c.Unassigned}");
    }
}
