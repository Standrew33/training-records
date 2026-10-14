using Skoleni.Core.Domain;
using Skoleni.Core.Models;
using static Skoleni.Tests.Support.Build;

namespace Skoleni.Tests;

public class PlannerTests
{
    private static readonly Course Kyber = Course("KYBER", 12);

    private static Plan PlanFor(Dataset ds) => Planner.Build(ds, Compliance.Compute(ds, Today, 90), Today);

    [Fact]
    public void Never_exceeds_free_seats_and_reports_the_rest_as_unassigned()
    {
        var ds = Set(Enumerable.Range(1, 6).Select(i => Emp($"E{i}")), [Kyber], terms: [Term("T1", Today.AddDays(7), capacity: 5, booked: 3), Term("T2", Today.AddDays(30), capacity: 2)]);
        var plan = PlanFor(ds);
        Assert.Equal(2, plan.ForTerm("T1").Count());
        Assert.Equal(2, plan.ForTerm("T2").Count());
        Assert.Equal(2, plan.Unassigned.Count);
        Assert.Equal(2, plan.Coverage.Single().Shortfall);
        Assert.Equal(4, plan.Coverage.Single().FreeSeats);
    }

    [Fact]
    public void Overbooked_term_has_zero_seats_not_negative()
    {
        var ds = Set([Emp("E1")], [Kyber], terms: [Term("T1", Today.AddDays(7), capacity: 30, booked: 31), Term("T2", Today.AddDays(40))]);
        var plan = PlanFor(ds);
        Assert.Equal("T2", plan.Assignments.Single().Term.Id);
        Assert.Equal(0, ds.Terms[0].FreeSeats);
    }

    [Fact]
    public void Excluded_cancelled_and_past_terms_are_never_used()
    {
        var ds = Set([Emp("E1")], [Kyber], terms: [Term("T1", Today.AddDays(5), excluded: "zrušeno"), Term("T2", Today.AddDays(-3)), Term("T3", Today.AddDays(20))]);
        Assert.Equal("T3", PlanFor(ds).Assignments.Single().Term.Id);
    }

    [Fact]
    public void Term_today_is_usable()
    {
        var ds = Set([Emp("E1")], [Kyber], terms: [Term("T1", Today)]);
        Assert.Equal("T1", PlanFor(ds).Assignments.Single().Term.Id);
    }

    [Fact]
    public void Most_urgent_gets_the_earliest_seat()
    {
        // E1 platnost končí za 10 dní, E2 za 60 – jedno místo na dřívějším termínu musí dostat E1.
        var ds = Set([Emp("E1"), Emp("E2")], [Kyber],
            [Done("E1", "KYBER", Today.AddDays(10).AddMonths(-12)), Done("E2", "KYBER", Today.AddDays(60).AddMonths(-12))],
            [Term("EARLY", Today.AddDays(5), capacity: 1), Term("LATE", Today.AddDays(30), capacity: 5)]);
        var plan = PlanFor(ds);
        Assert.Equal("EARLY", plan.Assignments.Single(a => a.Requirement.Employee.Id == "E1").Term.Id);
        Assert.Equal("LATE", plan.Assignments.Single(a => a.Requirement.Employee.Id == "E2").Term.Id);
        Assert.All(plan.Assignments, a => Assert.False(a.AfterDeadline));
    }

    [Fact]
    public void Warns_when_the_only_available_term_is_after_expiry()
    {
        var ds = Set([Emp("E1")], [Kyber], [Done("E1", "KYBER", Today.AddDays(10).AddMonths(-12))], [Term("T1", Today.AddDays(30))]);
        var a = PlanFor(ds).Assignments.Single();
        Assert.True(a.AfterDeadline);
        Assert.True(a.WillLapse);
    }

    [Fact]
    public void Already_expired_assignment_is_after_deadline_but_is_not_a_new_lapse()
    {
        var ds = Set([Emp("E1")], [Kyber], [Done("E1", "KYBER", Today.AddMonths(-20))], [Term("T1", Today.AddDays(5))]);
        var a = PlanFor(ds).Assignments.Single();
        Assert.True(a.AfterDeadline);
        Assert.False(a.WillLapse);
    }

    [Fact]
    public void Does_not_plan_parental_leave_terminated_or_valid_training()
    {
        var ds = Set([Emp("E1", state: EmployeeState.ParentalLeave), Emp("E2", state: EmployeeState.Terminated), Emp("E3")], [Kyber],
            [Done("E3", "KYBER", Today.AddMonths(-1))], [Term("T1", Today.AddDays(5))]);
        Assert.Empty(PlanFor(ds).Assignments);
    }

    [Fact]
    public void Does_not_book_a_term_after_the_employment_ends()
    {
        var ds = Set([Emp("E1", end: Today.AddDays(20))], [Kyber], terms: [Term("T1", Today.AddDays(30))]);
        var plan = PlanFor(ds);
        Assert.Empty(plan.Assignments);
        Assert.Single(plan.Unassigned);
    }

    [Fact]
    public void Uses_a_term_on_the_last_day_of_employment()
    {
        var ds = Set([Emp("E1", end: Today.AddDays(30))], [Kyber], terms: [Term("T1", Today.AddDays(30))]);
        Assert.Single(PlanFor(ds).Assignments);
    }

    [Fact]
    public void Same_person_is_not_booked_on_two_overlapping_terms_of_different_courses()
    {
        var gdpr = Course("GDPR", 36);
        var day = Today.AddDays(10);
        var ds = Set([Emp("E1")], [Kyber, gdpr], terms: [Term("T1", day, time: "09:00-12:00"), Term("T2", day, course: "GDPR", time: "10:00-12:00"), Term("T3", day.AddDays(7), course: "GDPR")]);
        var plan = PlanFor(ds);
        Assert.Equal(["T1", "T3"], plan.Assignments.Select(a => a.Term.Id).Order());
    }

    [Fact]
    public void Adjacent_terms_on_the_same_day_do_not_collide()
    {
        var gdpr = Course("GDPR", 36);
        var day = Today.AddDays(10);
        var ds = Set([Emp("E1")], [Kyber, gdpr], terms: [Term("T1", day, time: "09:00-12:00"), Term("T2", day, course: "GDPR", time: "12:00-14:00")]);
        Assert.Equal(2, PlanFor(ds).Assignments.Count);
    }

    [Fact]
    public void Course_without_any_term_is_reported_with_a_specific_reason()
    {
        var ds = Set([Emp("E1")], [Kyber]);
        Assert.Contains("žádný použitelný termín", PlanFor(ds).Unassigned.Single().Reason);
    }

    [Fact]
    public void Plan_is_deterministic()
    {
        var ds = Set(Enumerable.Range(1, 30).Select(i => Emp($"E{i:00}")), [Kyber], terms: [Term("T1", Today.AddDays(7), capacity: 12), Term("T2", Today.AddDays(30), capacity: 12)]);
        string Signature(Plan p) => string.Join(",", p.Assignments.Select(a => $"{a.Requirement.Employee.Id}>{a.Term.Id}"));
        Assert.Equal(Signature(PlanFor(ds)), Signature(PlanFor(ds)));
    }

    /// <summary>
    /// Důvěra v algoritmus: na náhodných malých instancích porovnáme plánovač s hrubou silou.
    /// Pokud existuje rozdělení, ve kterém všichni stihnou lhůtu, musí ho plánovač najít.
    /// </summary>
    [Fact]
    public void Finds_an_all_on_time_assignment_whenever_one_exists_property_test()
    {
        var rng = new Random(20261001);
        var feasibleRounds = 0;
        for (var round = 0; round < 400; round++)
        {
            var n = rng.Next(2, 7);
            var terms = Enumerable.Range(0, rng.Next(1, 4)).Select(i => Term($"T{i}", Today.AddDays(5 + i * rng.Next(5, 20)), capacity: rng.Next(1, 4))).ToList();
            var deadlines = Enumerable.Range(0, n).Select(_ => Today.AddDays(rng.Next(1, 70))).ToList();
            var emps = Enumerable.Range(0, n).Select(i => Emp($"E{i:00}")).ToList();
            var reqs = emps.Select((e, i) => Req(e, Kyber, TrainingStatus.ExpiringSoon, deadlines[i])).ToList();

            var plan = Planner.Build(Set(emps, [Kyber], terms: terms), reqs, Today);

            foreach (var t in terms) Assert.True(plan.ForTerm(t.Id).Count() <= t.FreeSeats, "kapacita");
            Assert.Equal(n, plan.Assignments.Count + plan.Unassigned.Count);

            if (!AllOnTimePossible(deadlines, terms)) continue;
            feasibleRounds++;
            Assert.Empty(plan.Unassigned);
            Assert.All(plan.Assignments, a => Assert.False(a.AfterDeadline, $"kolo {round}: {a.Requirement.Employee.Id} pozdě, ač řešení bez zpoždění existuje"));
        }
        Assert.True(feasibleRounds > 50, "test má pokrýt dost proveditelných instancí");
    }

    private static bool AllOnTimePossible(IReadOnlyList<DateOnly> deadlines, IReadOnlyList<Term> terms)
    {
        var seats = terms.Select(t => t.FreeSeats).ToArray();
        bool Place(int person)
        {
            if (person == deadlines.Count) return true;
            for (var t = 0; t < terms.Count; t++)
            {
                if (seats[t] == 0 || terms[t].Date > deadlines[person]) continue;
                seats[t]--;
                var ok = Place(person + 1);
                seats[t]++;
                if (ok) return true;
            }
            return false;
        }
        return Place(0);
    }
}
