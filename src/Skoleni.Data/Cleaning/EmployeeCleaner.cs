using System.Text.RegularExpressions;
using Skoleni.Core.Models;
using Skoleni.Data.Parsing;

namespace Skoleni.Data.Cleaning;

public sealed record EmployeeCleaningResult(IReadOnlyList<Employee> Employees, IReadOnlyDictionary<string, string> IdAliases);

public static partial class EmployeeCleaner
{
    public static EmployeeCleaningResult Clean(IReadOnlyList<CsvRow> rows, DateOnly exportDate, IssueLog log)
    {
        var aliases = FindDuplicates(rows, log);
        var departments = BuildDepartmentMap(rows);

        var drafts = new List<Draft>();
        var seenIds = new HashSet<string>();
        foreach (var row in rows)
        {
            var id = row.Trimmed("id_zamestnance");
            if (aliases.ContainsKey(id)) continue;
            if (!seenIds.Add(id))
            {
                log.Error(Sources.Employees, id, $"Employee ID repeats in the file (line {row.Line}).", "Second occurrence skipped.");
                continue;
            }
            drafts.Add(ToDraft(row, id, departments, exportDate, log));
        }

        var byEmail = drafts.Where(d => d.Email.Length > 0).GroupBy(d => d.Email).ToDictionary(g => g.Key, g => g.First().Id);
        foreach (var d in drafts.Where(d => d.ManagerEmail.Length > 0))
        {
            if (byEmail.TryGetValue(d.ManagerEmail, out var managerId) && managerId != d.Id) d.ManagerId = managerId;
            else log.Warn(Sources.Employees, d.Id, $"Manager \"{d.ManagerEmail}\" does not exist in the register.", "Employee is left without a manager (visible only in overall views).");
        }
        InferMissingDepartments(drafts, log);

        var employees = drafts.Select(d => new Employee(d.Id, d.FirstName, d.LastName, d.Email, d.Department, d.DepartmentInferred, d.Position, d.ManagerId, d.HireDate, d.TerminationDate, d.State)).ToList();
        return new EmployeeCleaningResult(employees, aliases);
    }

    /// <summary>
    /// A duplicate person = the same national identification number under a different ID (in the data: typos in the name,
    /// an e-mail with a "2"). The number is read only here, only for this match, and is never stored. The lowest ID is kept.
    /// </summary>
    private static Dictionary<string, string> FindDuplicates(IReadOnlyList<CsvRow> rows, IssueLog log)
    {
        var aliases = new Dictionary<string, string>();
        foreach (var group in rows.Where(r => r.Trimmed("rodne_cislo").Length > 0).GroupBy(r => r.Trimmed("rodne_cislo")))
        {
            var ids = group.Select(r => r.Trimmed("id_zamestnance")).Distinct().OrderBy(i => i, StringComparer.Ordinal).ToList();
            if (ids.Count < 2) continue;
            foreach (var dup in ids.Skip(1))
            {
                aliases[dup] = ids[0];
                log.Error(Sources.Employees, dup, $"Duplicate person: same personal identification number as {ids[0]} (different ID, name with typos).", $"Merged into {ids[0]}; training records are redirected.");
            }
        }
        return aliases;
    }

    /// <summary>Normalised key -> most frequent spelling. A shortened name ("Studijní") maps to the single longer one that starts with it ("Studijní oddělení").</summary>
    private static Dictionary<string, string> BuildDepartmentMap(IReadOnlyList<CsvRow> rows)
    {
        var map = rows.Select(r => r.Trimmed("utvar")).Where(s => s.Length > 0)
            .GroupBy(Text.Key)
            .ToDictionary(g => g.Key, g => g.GroupBy(x => x).OrderByDescending(v => v.Count()).First().Key);
        foreach (var key in map.Keys.ToList())
        {
            var longer = map.Keys.Where(o => o != key && o.StartsWith(key + " ")).ToList();
            if (longer.Count == 1) map[key] = map[longer[0]];
        }
        return map;
    }

    private static Draft ToDraft(CsvRow row, string id, Dictionary<string, string> departments, DateOnly exportDate, IssueLog log)
    {
        var rawEmail = row.Trimmed("email");
        var email = Regex.Replace(rawEmail, @"\s+", "").ToLowerInvariant();
        if (!EmailPattern().IsMatch(email))
            log.Warn(Sources.Employees, id, $"Invalid e-mail \"{rawEmail}\".", "Kept unchanged; not used for matching.");
        else if (email != rawEmail)
            log.Info(Sources.Employees, id, $"E-mail \"{rawEmail}\" contains whitespace.", "Whitespace removed.");

        var first = Text.TitleCase(row["jmeno"]);
        var last = Text.TitleCase(row["prijmeni"]);
        if (first != row["jmeno"] || last != row["prijmeni"])
            log.Info(Sources.Employees, id, $"Name \"{row["jmeno"]}\" / \"{row["prijmeni"]}\" has inconsistent letter case or whitespace.", "Normalised.");

        var hireRaw = row.Trimmed("datum_nastupu");
        DateOnly? hire = null;
        if (CzechDate.TryParse(hireRaw, out var h, out var kind))
        {
            hire = h;
            if (kind != CzechDate.Kind.Iso) log.Info(Sources.Employees, id, $"Hire date in a different format (\"{hireRaw}\").", $"Converted to {h:yyyy-MM-dd}.");
        }
        else log.Warn(Sources.Employees, id, $"Hire date \"{hireRaw}\" cannot be read.", "Hire date unknown.");
        var end = CzechDate.ParseOrNull(row["datum_ukonceni"]);

        var stateKey = Text.Key(row["stav"]);
        var state = stateKey.StartsWith("ukon") ? EmployeeState.Terminated : stateKey.StartsWith("rodic") ? EmployeeState.ParentalLeave : EmployeeState.Active;
        if (stateKey is not ("aktivni" or "") && state == EmployeeState.Active)
            log.Warn(Sources.Employees, id, $"Unknown status \"{row["stav"]}\".", "Treated as active.");
        if (hire > exportDate)
            log.Info(Sources.Employees, id, $"Starts only on {hire:yyyy-MM-dd} (after the export).", "Marked as starting; trainings are planned before the start.");
        if (state == EmployeeState.Active && end > exportDate)
            log.Info(Sources.Employees, id, $"Active, but with a planned termination on {end:yyyy-MM-dd}.", "The planner will not book a term after this date.");
        if (state == EmployeeState.Terminated && end is null)
            log.Warn(Sources.Employees, id, "Terminated employee without a termination date.", "Not counted in statistics.");

        var rawDept = row.Trimmed("utvar");
        var dept = "";
        if (rawDept.Length > 0)
        {
            dept = departments[Text.Key(rawDept)];
            if (dept != rawDept) log.Info(Sources.Employees, id, $"Department \"{rawDept}\" is spelled differently elsewhere.", $"Unified to \"{dept}\".");
        }

        return new Draft
        {
            Id = id, FirstName = first, LastName = last, Email = email, Department = dept, Position = row.Trimmed("pozice"),
            ManagerEmail = row.Trimmed("vedouci_email").ToLowerInvariant(), HireDate = hire, TerminationDate = end, State = state,
        };
    }

    /// <summary>Missing department: from the majority of subordinates (a manager), otherwise from the manager's department. Always with a warning to verify.</summary>
    private static void InferMissingDepartments(List<Draft> drafts, IssueLog log)
    {
        foreach (var d in drafts.Where(d => d.Department.Length == 0))
        {
            var fromSubordinates = drafts.Where(x => x.ManagerId == d.Id && x.Department.Length > 0)
                .GroupBy(x => x.Department).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            var fromManager = drafts.FirstOrDefault(x => x.Id == d.ManagerId)?.Department;
            var inferred = fromSubordinates ?? (string.IsNullOrEmpty(fromManager) ? null : fromManager);
            d.Department = inferred ?? "Unknown";
            d.DepartmentInferred = true;
            log.Warn(Sources.Employees, d.Id, "Department is missing.", inferred is null
                ? "Cannot be inferred, recorded as \"Unknown\"."
                : $"Inferred \"{inferred}\" ({(fromSubordinates is not null ? "from subordinates" : "from the manager")}); please verify.");
        }
    }

    [GeneratedRegex(@"^[^\s@,]+@[^\s@,]+\.[a-z]{2,}$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailPattern();

    private sealed class Draft
    {
        public string Id = "", FirstName = "", LastName = "", Email = "", Department = "", Position = "", ManagerEmail = "";
        public string? ManagerId;
        public bool DepartmentInferred;
        public DateOnly? HireDate, TerminationDate;
        public EmployeeState State;
    }
}
