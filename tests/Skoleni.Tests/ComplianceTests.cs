using Skoleni.Core.Domain;
using Skoleni.Core.Models;
using static Skoleni.Tests.Support.Build;

namespace Skoleni.Tests;

public class ComplianceTests
{
    private static readonly Course Kyber = Course("KYBER", 12);

    [Fact]
    public void Validity_is_inclusive_on_the_last_day()
    {
        var lastDay = Compliance.Evaluate(Emp("E1"), Kyber, Today.AddMonths(-12), Today, soonDays: 0);
        Assert.Equal(TrainingStatus.ExpiringSoon, lastDay.Status); // platí ještě dnes (0 dní zbývá), ne „prošlé“
        Assert.Equal(0, lastDay.DaysLeft);
        var dayAfter = Compliance.Evaluate(Emp("E1"), Kyber, Today.AddMonths(-12).AddDays(-1), Today, soonDays: 0);
        Assert.Equal(TrainingStatus.Expired, dayAfter.Status);
        Assert.Equal(-1, dayAfter.DaysLeft);
    }

    [Theory]
    [InlineData(-400, TrainingStatus.Expired)]
    [InlineData(-1, TrainingStatus.Expired)]
    [InlineData(0, TrainingStatus.ExpiringSoon)]
    [InlineData(90, TrainingStatus.ExpiringSoon)]
    [InlineData(91, TrainingStatus.Valid)]
    public void Classifies_by_days_left(int daysLeft, TrainingStatus expected)
    {
        var completed = Today.AddDays(daysLeft).AddMonths(-12);
        Assert.Equal(expected, Compliance.Evaluate(Emp("E1"), Kyber, completed, Today, soonDays: 90).Status);
    }

    [Fact]
    public void Month_end_is_clamped_not_overflowed()
    {
        // 29. 2. 2024 + 12 měsíců = 28. 2. 2025, ne 1. 3. 2025
        var r = Compliance.Evaluate(Emp("E1"), Kyber, new DateOnly(2024, 2, 29), new DateOnly(2025, 1, 1), 30);
        Assert.Equal(new DateOnly(2025, 2, 28), r.ValidUntil);
    }

    [Fact]
    public void Course_without_expiry_is_valid_forever_once_done_and_missing_otherwise()
    {
        var lead = Course("LEAD", 0);
        Assert.Equal(TrainingStatus.Valid, Compliance.Evaluate(Emp("E1"), lead, new DateOnly(2001, 1, 1), Today, 90).Status);
        Assert.Equal(TrainingStatus.Missing, Compliance.Evaluate(Emp("E1"), lead, null, Today, 90).Status);
    }

    [Fact]
    public void Missing_training_is_due_today_for_long_serving_employee()
    {
        var r = Compliance.Evaluate(Emp("E1", hire: new DateOnly(2015, 1, 1)), Kyber, null, Today, 90);
        Assert.Equal(Today, r.Deadline);
    }

    [Fact]
    public void New_hire_gets_grace_period_after_start()
    {
        var hire = Today.AddDays(5);
        var r = Compliance.Evaluate(Emp("E1", hire: hire), Kyber, null, Today, 90);
        Assert.Equal(hire.AddDays(Compliance.DefaultHireGraceDays), r.Deadline);
    }

    [Fact]
    public void Uses_latest_completion_and_ignores_completions_after_the_reference_date()
    {
        var ds = Set([Emp("E1")], [Kyber], [Done("E1", "KYBER", Today.AddMonths(-30)), Done("E1", "KYBER", Today.AddMonths(-3)), Done("E1", "KYBER", Today.AddDays(10))]);
        var r = Compliance.Compute(ds, Today, 30).Single();
        Assert.Equal(Today.AddMonths(-3), r.LastCompleted);
        Assert.Equal(TrainingStatus.Valid, r.Status);
    }

    [Fact]
    public void Only_applicable_courses_are_required_exact_position_and_department_match()
    {
        var courses = new[]
        {
            Course("ALL", 12), Course("LAB", 12, AudienceKind.Department, "Laboratoře"),
            Course("TECH", 12, AudienceKind.Position, "Technik údržby"), Course("OPT", null, AudienceKind.Optional),
        };
        var emps = new[] { Emp("E1", "Laboratoře", "Technik"), Emp("E2", "Správa budov", "Technik údržby") };
        var reqs = Compliance.Compute(Set(emps, courses), Today, 30);
        Assert.Equal(["ALL", "LAB"], reqs.Where(r => r.Employee.Id == "E1").Select(r => r.Course.Code).Order());
        Assert.Equal(["ALL", "TECH"], reqs.Where(r => r.Employee.Id == "E2").Select(r => r.Course.Code).Order()); // „Technik“ ≠ „Technik údržby“
    }

    [Fact]
    public void Terminated_people_are_excluded_but_parental_leave_and_starters_are_kept_and_labelled()
    {
        var emps = new[]
        {
            Emp("E1", state: EmployeeState.Terminated), Emp("E2", end: Today), // ukončení dnes = už nepracuje
            Emp("E3", end: Today.AddDays(1)), Emp("E4", state: EmployeeState.ParentalLeave), Emp("E5", hire: Today.AddDays(3)),
        };
        var reqs = Compliance.Compute(Set(emps, [Kyber]), Today, 30);
        Assert.Equal(["E3", "E4", "E5"], reqs.Select(r => r.Employee.Id).Order());
        Assert.Equal(EmployeeGroup.ParentalLeave, Compliance.GroupOf(emps[3], Today));
        Assert.Equal(EmployeeGroup.Starting, Compliance.GroupOf(emps[4], Today));
    }

    [Fact]
    public void Status_counts_always_add_up_to_requirements()
    {
        var ds = Set(Enumerable.Range(1, 40).Select(i => Emp($"E{i}")), [Kyber, Course("GDPR", 36)],
            Enumerable.Range(1, 40).Where(i => i % 3 != 0).Select(i => Done($"E{i}", "KYBER", Today.AddDays(-i * 13))));
        var reqs = Compliance.Compute(ds, Today, 60);
        Assert.Equal(80, reqs.Count);
        Assert.Equal(reqs.Count, Enum.GetValues<TrainingStatus>().Sum(s => reqs.Count(r => r.Status == s)));
    }
}
