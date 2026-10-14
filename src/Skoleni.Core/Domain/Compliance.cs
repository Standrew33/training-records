using Skoleni.Core.Models;

namespace Skoleni.Core.Domain;

public enum TrainingStatus { Valid, ExpiringSoon, Expired, Missing }

public enum EmployeeGroup { Active, Starting, ParentalLeave, Terminated }

/// <summary>Jedna povinnost jednoho zaměstnance: „má/nemá platné školení X k danému dni“.</summary>
/// <param name="Deadline">Do kdy musí být školení absolvováno. null = bez expirace / nic nehrozí.</param>
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
    /// Předpoklad: nový zaměstnanec (nebo zaměstnanec bez jakéhokoli záznamu) má na povinné školení
    /// tolik dní od nástupu. Zdroj žádnou lhůtu neuvádí, takže jde o konfigurovatelnou volbu, ne fakt.
    /// </summary>
    public const int DefaultHireGraceDays = 30;

    public static EmployeeGroup GroupOf(Employee e, DateOnly asOf)
    {
        if (e.State == EmployeeState.Terminated || (e.TerminationDate is { } end && end <= asOf)) return EmployeeGroup.Terminated;
        if (e.State == EmployeeState.ParentalLeave) return EmployeeGroup.ParentalLeave;
        if (e.HireDate is { } hire && hire > asOf) return EmployeeGroup.Starting;
        return EmployeeGroup.Active;
    }

    /// <summary>
    /// Platnost se počítá z data absolvování a katalogu (ne ze sloupce platnost_do v exportu, který je nespolehlivý).
    /// Poslední den platnosti je včetně: školení platné do 10. 5. je 10. 5. ještě platné.
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
            // Nikdy absolvováno: lhůta = nástup + odklad, nejdřív však dnes (už je pozdě).
            var due = (emp.HireDate ?? asOf).AddDays(hireGraceDays);
            var deadline = emp.HireDate is not null && due > asOf ? due : asOf;
            return new Requirement(emp, course, TrainingStatus.Missing, null, null, null, deadline);
        }
        if (!course.Expires)
            return new Requirement(emp, course, TrainingStatus.Valid, lastCompleted, null, null, null);

        var validUntil = lastCompleted.Value.AddMonths(course.ValidityMonths!.Value); // DateOnly ořízne konec měsíce
        var daysLeft = validUntil.DayNumber - asOf.DayNumber;
        var status = daysLeft < 0 ? TrainingStatus.Expired : daysLeft <= soonDays ? TrainingStatus.ExpiringSoon : TrainingStatus.Valid;
        return new Requirement(emp, course, status, lastCompleted, validUntil, daysLeft, validUntil);
    }
}
