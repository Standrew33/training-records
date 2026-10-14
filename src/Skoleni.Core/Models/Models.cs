namespace Skoleni.Core.Models;

// The cleaned data model. None of these types carries a national identification number or free-text notes
// from the source exports - that is deliberate (data minimisation), not an omission.

public enum EmployeeState { Active, ParentalLeave, Terminated }

public sealed record Employee(
    string Id,
    string FirstName,
    string LastName,
    string Email,
    string Department,
    bool DepartmentInferred,
    string Position,
    string? ManagerId,
    DateOnly? HireDate,
    DateOnly? TerminationDate,
    EmployeeState State)
{
    public string FullName => $"{LastName} {FirstName}";
}

public enum AudienceKind { Everyone, Department, Position, Optional }

/// <summary>Who must take a course. Values are already normalised to the canonical spelling of department / position.</summary>
public sealed record Audience(AudienceKind Kind, IReadOnlyList<string> Values)
{
    public bool Applies(Employee e) => Kind switch
    {
        AudienceKind.Everyone => true,
        AudienceKind.Department => Values.Contains(e.Department, StringComparer.OrdinalIgnoreCase),
        AudienceKind.Position => Values.Contains(e.Position, StringComparer.OrdinalIgnoreCase),
        _ => false,
    };
}

/// <param name="ValidityMonths">null = not stated (optional course), 0 = one-off course that never expires.</param>
public sealed record Course(string Code, string Name, int? ValidityMonths, Audience Audience)
{
    public bool Expires => ValidityMonths is > 0;
}

public sealed record Completion(
    string Id,
    string EmployeeId,
    string CourseCode,
    DateOnly Date,
    DateOnly? SourceValidUntil,
    decimal? Price);

public sealed record Term(
    string Id,
    string CourseCode,
    DateOnly? Date,
    TimeOnly? Start,
    TimeOnly? End,
    string Place,
    int Capacity,
    int Booked,
    decimal? Price,
    bool PriceEstimated,
    string? ExcludedReason,
    bool Weekend)
{
    /// <summary>Free seats. An overbooked term (booked &gt; capacity) has 0, never a negative number.</summary>
    public int FreeSeats => Math.Max(0, Capacity - Booked);

    public bool Usable => ExcludedReason is null && Date is not null;

    public bool Overlaps(Term other) =>
        Date is not null && Date == other.Date &&
        (Start is null || End is null || other.Start is null || other.End is null
            ? true // unknown time: assume a collision rather than risk a double booking
            : Start < other.End && other.Start < End);
}

public enum IssueSeverity { Info, Warning, Error }

/// <summary>One recorded defect in the input data and how it was handled.</summary>
public sealed record Issue(string Source, IssueSeverity Severity, string Ref, string Description, string Resolution);

public sealed record Dataset(
    DateOnly ExportDate,
    IReadOnlyList<Employee> Employees,
    IReadOnlyList<Course> Courses,
    IReadOnlyList<Completion> Completions,
    IReadOnlyList<Term> Terms,
    IReadOnlyList<Issue> Issues);
