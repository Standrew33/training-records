using Skoleni.Core.Models;

namespace Skoleni.Core.Domain;

/// <param name="AfterDeadline">The term falls after the deadline (for a training about to expire: after it expires). Always true for already expired / missing trainings.</param>
public sealed record Assignment(Requirement Requirement, Term Term, bool AfterDeadline)
{
    /// <summary>The important warning: the training is still valid today, but the assigned term is after it lapses.</summary>
    public bool WillLapse => AfterDeadline && Requirement.Status == TrainingStatus.ExpiringSoon;
}

public sealed record Unassigned(Requirement Requirement, string Reason);

public sealed record CourseCoverage(string CourseCode, int Needed, int FreeSeats, int Unassigned)
{
    public int Shortfall => Unassigned;
}

public sealed class Plan
{
    public required IReadOnlyList<Assignment> Assignments { get; init; }
    public required IReadOnlyList<Unassigned> Unassigned { get; init; }
    public required IReadOnlyList<CourseCoverage> Coverage { get; init; }

    public decimal EstimatedCost => Assignments.Sum(a => a.Term.Price ?? 0m);

    public IEnumerable<Assignment> ForTerm(string termId) => Assignments.Where(a => a.Term.Id == termId);
}

/// <summary>
/// Recommendation of who to sign up for which term.
///
/// Algorithm (earliest-deadline-first; unit jobs on seats with fixed times):
///  1. demands are sorted by deadline (expired and missing ones have deadline "today", the others their expiry date),
///  2. each gets the earliest term with a free seat that meets the deadline,
///  3. if no free term meets the deadline, it gets the earliest free one and is marked AfterDeadline,
///  4. if there is no free seat at all, it ends up in Unassigned with a reason (a signal to schedule another term).
///
/// The property trust rests on: if an assignment exists in which everybody meets the deadline, the planner finds it
/// (EDF is optimal for this problem; verified by a property test against brute force).
/// A deliberate choice: people already past the deadline go first - it minimises total delay in days, even if it may
/// cost somebody whose validity is only about to end a "new" lapse.
///
/// Hard constraints: capacity (free seats = capacity - booked, never negative), no booking after the employment ends,
/// no two colliding terms for one person. Parental leave and terminated employees are not planned.
/// </summary>
public static class Planner
{
    public static Plan Build(Dataset ds, IReadOnlyList<Requirement> requirements, DateOnly asOf)
    {
        var terms = ds.Terms.Where(t => t.Usable && t.Date >= asOf).OrderBy(t => t.Date).ThenBy(t => t.Start).ThenBy(t => t.Id, StringComparer.Ordinal).ToList();
        var seatsLeft = terms.ToDictionary(t => t.Id, t => t.FreeSeats);

        var demand = requirements
            .Where(r => r.NeedsAction && Compliance.GroupOf(r.Employee, asOf) is EmployeeGroup.Active or EmployeeGroup.Starting)
            .OrderBy(r => r.Deadline ?? DateOnly.MaxValue)
            // with equal deadlines the course with fewer terms to choose from goes first - otherwise a person could
            // "use up" the only term needed for their other course (collision at the same time)
            .ThenBy(r => terms.Count(t => t.CourseCode == r.Course.Code))
            .ThenBy(r => r.Employee.Id, StringComparer.Ordinal)
            .ThenBy(r => r.Course.Code, StringComparer.Ordinal)
            .ToList();

        var assignments = new List<Assignment>();
        var unassigned = new List<Unassigned>();
        var taken = new Dictionary<string, List<Term>>();

        foreach (var req in demand)
        {
            var mine = taken.GetValueOrDefault(req.Employee.Id) ?? [];
            var candidates = terms.Where(t =>
                t.CourseCode == req.Course.Code &&
                seatsLeft[t.Id] > 0 &&
                (req.Employee.TerminationDate is not { } end || t.Date <= end) &&
                !mine.Any(o => o.Overlaps(t))).ToList();

            if (candidates.Count == 0)
            {
                var anyTerm = terms.Any(t => t.CourseCode == req.Course.Code);
                unassigned.Add(new Unassigned(req, anyTerm
                    ? "All terms are full, collide with another term, or fall after the employment ends."
                    : "No usable term is scheduled."));
                continue;
            }

            var inTime = candidates.FirstOrDefault(t => req.Deadline is null || t.Date <= req.Deadline);
            var chosen = inTime ?? candidates[0];
            seatsLeft[chosen.Id]--;
            if (!taken.TryGetValue(req.Employee.Id, out var list)) taken[req.Employee.Id] = list = [];
            list.Add(chosen);
            assignments.Add(new Assignment(req, chosen, AfterDeadline: inTime is null));
        }

        var coverage = ds.Courses
            .Select(c => new CourseCoverage(
                c.Code,
                demand.Count(r => r.Course.Code == c.Code),
                terms.Where(t => t.CourseCode == c.Code).Sum(t => t.FreeSeats),
                unassigned.Count(u => u.Requirement.Course.Code == c.Code)))
            .Where(c => c.Needed > 0)
            .ToList();

        return new Plan { Assignments = assignments, Unassigned = unassigned, Coverage = coverage };
    }
}
