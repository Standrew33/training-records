using Skoleni.Core.Models;

namespace Skoleni.Core.Domain;

/// <param name="AfterDeadline">Termín je až po lhůtě (u končící platnosti = po expiraci). U už prošlých / chybějících je to vždy pravda.</param>
public sealed record Assignment(Requirement Requirement, Term Term, bool AfterDeadline)
{
    /// <summary>Důležité varování: školení ještě platí, ale přidělený termín je až po vypršení.</summary>
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
/// Doporučení, koho přihlásit na který termín.
///
/// Algoritmus (earliest-deadline-first, jednotkové úlohy na místech s pevným časem):
///  1. potřeby se seřadí podle lhůty (prošlé a chybějící mají lhůtu „dnes“, ostatní datum expirace),
///  2. každá dostane nejbližší termín s volným místem, který lhůtu stihne,
///  3. když žádný volný termín lhůtu nestihne, dostane nejbližší volný a je označena AfterDeadline,
///  4. když volné místo není vůbec, skončí v Unassigned s důvodem (signál pro vypsání dalšího termínu).
///
/// Vlastnost, na které stojí důvěra: pokud existuje přiřazení, kde všichni stihnou lhůtu, plánovač ho najde
/// (EDF je pro tuto úlohu optimální; ověřeno property-testem proti hrubé síle).
/// Vědomá volba: nejdřív ti, kdo už jsou po lhůtě – minimalizuje to celkové zpoždění ve dnech, i když tím
/// případně o „nový“ prošlý termín přijde někdo, komu platnost teprve končí.
///
/// Tvrdá omezení: kapacita (volné místo = kapacita − obsazeno, nikdy záporné), nepřihlašovat na termín po datu
/// ukončení poměru, nepřihlašovat jednoho člověka na dva kolidující termíny. Rodičovská a ukončení se neplánují.
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
            // při shodné lhůtě dřív to školení, které má méně termínů na výběr – jinak by si člověk
            // mohl „zabrat“ jediný termín, který potřeboval pro druhé školení (kolize ve stejný čas)
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
                    ? "Všechny termíny jsou plné, kolidují, nebo jsou až po ukončení pracovního poměru."
                    : "Není vypsán žádný použitelný termín."));
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
