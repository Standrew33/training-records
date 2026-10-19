using Skoleni.Core.Models;

namespace Skoleni.Core.Domain;

public enum TrainingStatus { Valid, ExpiringSoon, Expired, Missing }

public enum EmployeeGroup { Active, Starting, ParentalLeave, Terminated }

/// <summary>One obligation of one employee: "has / has not a valid training X on the given day".</summary>
/// <param name="Deadline">By when the training must be completed. null = no expiry, nothing at stake.</param>
public sealed record Requirement(
    Employee Employee,
    Course Course,
    TrainingStatus Status,
    DateOnly? LastCompleted,
    DateOnly? ValidUntil,
    int? DaysLeft,
    DateOnly? Deadline)
{
    public bool NeedsAction => Status != TrainingStatus.Valid;
}

public static class Compliance
{
    /// <summary>
    /// Assumption: a new hire (or an employee with no record at all) has this many days from the start date to complete
    /// a mandatory training. The source states no deadline, so this is a configurable choice, not a fact.
    /// </summary>
    public const int DefaultHireGraceDays = 30;

    public static EmployeeGroup GroupOf(Employee e, DateOnly asOf)
    {
        if (e.State == EmployeeState.Terminated || (e.TerminationDate is { } end && end <= asOf)) 
            return EmployeeGroup.Terminated;
        if (e.State == EmployeeState.ParentalLeave) 
            return EmployeeGroup.ParentalLeave;
        if (e.HireDate is { } hire && hire > asOf) 
            return EmployeeGroup.Starting;
        return EmployeeGroup.Active;
    }

    /// <summary>
    /// Validity is computed from the completion date and the catalogue (not from the export's platnost_do column, which is unreliable).
    /// The last day of validity is inclusive: a training valid until 10 May is still valid on 10 May.
    /// </summary>
    public static IReadOnlyList<Requirement> Compute(Dataset ds, DateOnly asOf, int soonDays, int hireGraceDays = DefaultHireGraceDays)
    {
        var latest = ds.Completions
            .Where(c => c.Date <= asOf)
            .GroupBy(c => (c.EmployeeId, c.CourseCode))
            .ToDictionary(g => g.Key, g => g.Max(c => c.Date));

        var result = new List<Requirement>();
        foreach (var emp in ds.Employees)
        {
            if (GroupOf(emp, asOf) == EmployeeGroup.Terminated) continue;
            foreach (var course in ds.Courses.Where(c => c.Audience.Applies(emp)))
                result.Add(Evaluate(emp, course, latest.TryGetValue((emp.Id, course.Code), out var d) ? d : null, asOf, soonDays, hireGraceDays));
        }
        return result;
    }

    public static Requirement Evaluate(Employee emp, Course course, DateOnly? lastCompleted, DateOnly asOf, int soonDays, int hireGraceDays = DefaultHireGraceDays)
    {
        if (lastCompleted is null)
        {
            // Never completed: deadline = start date + grace period, but no earlier than today (it is already late).
            var due = (emp.HireDate ?? asOf).AddDays(hireGraceDays);
            var deadline = emp.HireDate is not null && due > asOf ? due : asOf;

            return new Requirement(emp, course, TrainingStatus.Missing, null, null, null, deadline);
        }

        if (!course.Expires)
            return new Requirement(emp, course, TrainingStatus.Valid, lastCompleted, null, null, null);

        var validUntil = lastCompleted.Value.AddMonths(course.ValidityMonths!.Value); // DateOnly clamps to the end of the month
        var daysLeft = validUntil.DayNumber - asOf.DayNumber;
        var status = daysLeft < 0 ? TrainingStatus.Expired : daysLeft <= soonDays ? TrainingStatus.ExpiringSoon : TrainingStatus.Valid;

        return new Requirement(emp, course, status, lastCompleted, validUntil, daysLeft, validUntil);
    }
}
