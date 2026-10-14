using Skoleni.Core.Domain;
using Skoleni.Core.Models;

namespace Skoleni.Tests.Support;

/// <summary>Test builders: short factories so that a test shows only what matters.</summary>
internal static class Build
{
    public static readonly DateOnly Today = new(2026, 10, 1);

    public static Employee Emp(string id, string dept = "IT oddělení", string position = "Technik",
        EmployeeState state = EmployeeState.Active, DateOnly? hire = null, DateOnly? end = null, string? manager = null) =>
        new(id, "Jan", id, $"{id.ToLowerInvariant()}@x.example", dept, false, position, manager, hire ?? new DateOnly(2015, 1, 1), end, state);

    public static Course Course(string code = "KYBER", int? months = 12, AudienceKind kind = AudienceKind.Everyone, params string[] values) =>
        new(code, code, months, new Audience(kind, values));

    public static Completion Done(string emp, string course, DateOnly date) =>
        new($"Z{emp}{course}{date:yyyyMMdd}", emp, course, date, null, null);

    public static Term Term(string id, DateOnly date, int capacity = 10, int booked = 0, string course = "KYBER",
        string time = "09:00-12:00", string? excluded = null)
    {
        var parts = time.Split('-');
        return new Term(id, course, date, TimeOnly.Parse(parts[0]), TimeOnly.Parse(parts[1]), "Aula", capacity, booked, 800m, false, excluded, false);
    }

    public static Dataset Set(IEnumerable<Employee> emps, IEnumerable<Course> courses, IEnumerable<Completion>? done = null, IEnumerable<Term>? terms = null) =>
        new(Today, emps.ToList(), courses.ToList(), (done ?? []).ToList(), (terms ?? []).ToList(), []);

    public static Requirement Req(Employee e, Course c, TrainingStatus status, DateOnly? deadline) =>
        new(e, c, status, null, deadline, deadline is null ? null : deadline.Value.DayNumber - Today.DayNumber, deadline);
}
