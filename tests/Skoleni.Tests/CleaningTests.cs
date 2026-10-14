using Skoleni.Core.Models;
using Skoleni.Data;
using Skoleni.Data.Cleaning;
using Skoleni.Data.Parsing;

namespace Skoleni.Tests;

/// <summary>Every kind of defect found in the real exports has a small sample here and a test that it is always handled the same way, and out loud.</summary>
public class CleaningTests
{
    private static readonly DateOnly Export = new(2026, 10, 1);

    private const string EmployeeHeader = "id_zamestnance,jmeno,prijmeni,email,utvar,pozice,vedouci_email,datum_nastupu,datum_ukonceni,stav,rodne_cislo,poznamka\n";
    private const string CompletionHeader = "id_zaznamu;id_zamestnance;zamestnanec;kod_skoleni;datum_absolvovani;platnost_do;poskytovatel;cena_kc;poznamka\n";
    private const string CatalogHeader = "kod,nazev,platnost_mesicu,povinne_pro,poznamka\n";
    private const string TermHeader = "id_terminu;kod_skoleni;datum;cas;misto;kapacita;obsazeno;cena_na_osobu;stav\n";

    private const string Catalog =
        CatalogHeader + "KYBER,Kyber,12,VSICHNI,\nGDPR,GDPR,3 roky,VSICHNI,\nLAB,Lab,24,UTVAR:Laboratoře,\nLEAD,Vedení,0,POZICE:Vedoucí,\nANGL,Angličtina,,NEPOVINNE,\n";

    private static Dataset Build(string employees = "", string completions = "", string terms = "", string catalog = Catalog) =>
        DatasetBuilder.Build(new RawExports(EmployeeHeader + employees, CompletionHeader + completions, catalog, TermHeader + terms), Export);

    private static IEnumerable<Issue> IssuesAbout(Dataset ds, string reference) => ds.Issues.Where(i => i.Ref.StartsWith(reference));

    // ---------- employees ----------

    [Fact]
    public void Merges_duplicate_person_by_identity_number_and_redirects_their_records()
    {
        var ds = Build(
            employees: "E0155,Adam,Novotný,adam@x.example,Personální oddělení,Referent,,2018-04-21,,aktivní,670501/6715,\n" +
                       "E1000,Adam,Novotny,adam2@x.example,Personální oddělení,Referent,,2018-04-21,,aktivní,670501/6715,\n",
            completions: "Z1;E1000;Novotny Adam;KYBER;2025-03-20;2026-03-20;X;0,00;\n");
        Assert.Equal(["E0155"], ds.Employees.Select(e => e.Id));
        Assert.Equal("E0155", ds.Completions.Single().EmployeeId);
        Assert.Contains(ds.Issues, i => i.Ref == "E1000" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void Normalizes_names_departments_and_emails()
    {
        var ds = Build(employees:
            "E1,  LUCIE,růžička,lucie @x.example,Správa budov,Technik údržby,,2010-01-01,,aktivní,1,\n" +
            "E2,Jan,Novák,jan@x.example,SPRÁVA BUDOV,Technik údržby,,2010-01-01,,aktivní,2,\n" +
            "E3,Eva,Malá,eva@x.example,Sprava budov,Technik údržby,,2010-01-01,,aktivní,3,\n");
        Assert.Equal("Lucie", ds.Employees[0].FirstName);
        Assert.Equal("Růžička", ds.Employees[0].LastName);
        Assert.Equal("lucie@x.example", ds.Employees[0].Email);
        Assert.Single(ds.Employees.Select(e => e.Department).Distinct());
    }

    [Fact]
    public void Short_department_name_is_merged_into_the_unique_longer_one()
    {
        var ds = Build(employees:
            "E1,A,A,a@x.example,Studijní oddělení,Referent,,2010-01-01,,aktivní,1,\n" +
            "E2,B,B,b@x.example,Studijní oddělení,Referent,,2010-01-01,,aktivní,2,\n" +
            "E3,C,C,c@x.example,Studijní,Referent,,2010-01-01,,aktivní,3,\n");
        Assert.All(ds.Employees, e => Assert.Equal("Studijní oddělení", e.Department));
    }

    [Fact]
    public void Missing_department_is_inferred_from_subordinates_first_then_from_manager_and_flagged()
    {
        var ds = Build(employees:
            "E1,Šéf,Lab,sef@x.example,,Vedoucí,ved@x.example,2010-01-01,,aktivní,1,\n" +
            "E2,A,A,a@x.example,Laboratoře,Laborant,sef@x.example,2010-01-01,,aktivní,2,\n" +
            "E3,B,B,b@x.example,Laboratoře,Laborant,sef@x.example,2010-01-01,,aktivní,3,\n" +
            "E4,C,C,c@x.example,,Laborant,sef@x.example,2010-01-01,,aktivní,4,\n" +
            "E5,V,V,ved@x.example,Vedení,Vedoucí,,2010-01-01,,aktivní,5,\n");
        var boss = ds.Employees.Single(e => e.Id == "E1");
        Assert.Equal("Laboratoře", boss.Department);   // from subordinates, not from the manager (Vedení)
        Assert.True(boss.DepartmentInferred);
        Assert.Equal("Laboratoře", ds.Employees.Single(e => e.Id == "E4").Department); // from the manager
        Assert.Equal(2, ds.Issues.Count(i => i.Severity == IssueSeverity.Warning && i.Description == "Department is missing."));
    }

    [Fact]
    public void Unknown_manager_is_reported_and_left_empty()
    {
        var ds = Build(employees: "E1,A,A,a@x.example,IT,Technik,nikdo@x.example,2010-01-01,,aktivní,1,\n");
        Assert.Null(ds.Employees.Single().ManagerId);
        Assert.Contains(ds.Issues, i => i.Ref == "E1" && i.Description.Contains("Manager"));
    }

    [Fact]
    public void Reads_all_hire_date_formats_and_states_and_keeps_future_hire()
    {
        var ds = Build(employees:
            "E1,A,A,a@x.example,IT,T,,3. 8. 2019,,aktivní,1,\n" +
            "E2,B,B,b@x.example,IT,T,,2026-12-01,,aktivní,2,\n" +
            "E3,C,C,c@x.example,IT,T,,2015-01-01,,rodičovská,3,\n" +
            "E4,D,D,d@x.example,IT,T,,2015-01-01,2025-12-31,ukončen,4,\n");
        Assert.Equal(new DateOnly(2019, 8, 3), ds.Employees[0].HireDate);
        Assert.Equal(new DateOnly(2026, 12, 1), ds.Employees[1].HireDate);
        Assert.Equal(EmployeeState.ParentalLeave, ds.Employees[2].State);
        Assert.Equal(EmployeeState.Terminated, ds.Employees[3].State);
    }

    // ---------- katalog ----------

    [Fact]
    public void Catalog_validity_in_words_and_zero_are_understood()
    {
        var ds = Build(employees: "E1,A,A,a@x.example,Laboratoře,Vedoucí,,2010-01-01,,aktivní,1,\n");
        Assert.Equal(36, ds.Courses.Single(c => c.Code == "GDPR").ValidityMonths);
        Assert.Equal(0, ds.Courses.Single(c => c.Code == "LEAD").ValidityMonths);
        Assert.Null(ds.Courses.Single(c => c.Code == "ANGL").ValidityMonths);
        Assert.Equal(AudienceKind.Optional, ds.Courses.Single(c => c.Code == "ANGL").Audience.Kind);
    }

    [Fact]
    public void Catalog_audience_values_are_matched_to_the_spelling_used_in_the_register()
    {
        var ds = Build(employees: "E1,A,A,a@x.example,Laboratoře,T,,2010-01-01,,aktivní,1,\n",
            catalog: CatalogHeader + "LAB,Lab,24,UTVAR:Laboratore;Neexistuje,\n");
        var lab = ds.Courses.Single();
        Assert.Contains("Laboratoře", lab.Audience.Values);
        Assert.Contains(ds.Issues, i => i.Source == Sources.Catalog && i.Description.Contains("Neexistuje"));
        Assert.True(lab.Audience.Applies(ds.Employees.Single()));
    }

    // ---------- completed trainings ----------

    private const string OneEmployee = "E1,A,A,a@x.example,IT,Technik,,2010-01-01,,aktivní,1,\n";

    [Fact]
    public void Drops_non_record_rows_comment_header_and_total()
    {
        var ds = Build(OneEmployee, completions:
            "# export z 1. 10. 2026 – nezapočítávat;;;;;;;;\n" +
            "Z1;E1;A;KYBER;2025-01-01;2026-01-01;X;0,00;\n" +
            "id_zaznamu;id_zamestnance;zamestnanec;kod_skoleni;datum_absolvovani;platnost_do;poskytovatel;cena_kc;poznamka\n" +
            "Celkem;;;;;;;2865;\n");
        Assert.Single(ds.Completions);
    }

    [Fact]
    public void Placeholder_1900_and_impossible_dates_are_dropped_not_guessed()
    {
        var ds = Build(OneEmployee, completions: "Z1;E1;A;KYBER;1900-01-01;1901-01-01;X;0,00;\nZ2;E1;A;KYBER;31. 9. 2025;;X;0,00;\n");
        Assert.Empty(ds.Completions);
        Assert.Equal(2, ds.Issues.Count(i => i.Source == Sources.Completions && i.Severity == IssueSeverity.Error));
    }

    [Fact]
    public void Excel_serial_date_is_converted()
    {
        var ds = Build(OneEmployee, completions: "Z1;E1;A;KYBER;43552;43917;X;0,00;\n");
        Assert.Equal(new DateOnly(2019, 3, 28), ds.Completions.Single().Date);
    }

    [Theory]
    [InlineData("kyber", "KYBER")]
    [InlineData("KYB3R", "KYBER")]
    [InlineData("GDPR ", "GDPR")]
    public void Fixes_unambiguous_course_codes(string written, string expected)
    {
        var ds = Build(OneEmployee, completions: $"Z1;E1;A;{written};2025-01-01;;X;0,00;\n");
        Assert.Equal(expected, ds.Completions.Single().CourseCode);
    }

    [Theory]
    [InlineData("GDPR-2")]
    [InlineData("KYBR-X")]
    [InlineData("")]
    public void Does_not_guess_ambiguous_course_codes(string written)
    {
        var ds = Build(OneEmployee, completions: $"Z1;E1;A;{written};2025-01-01;;X;0,00;\n");
        Assert.Empty(ds.Completions);
        Assert.Contains(ds.Issues, i => i.Severity == IssueSeverity.Error && i.Description.Contains("Unknown course code"));
    }

    [Fact]
    public void Orphan_records_are_dropped_and_reported()
    {
        var ds = Build(OneEmployee, completions: "Z1;E9001;Neznámý;KYBER;2025-01-01;;X;0,00;\n");
        Assert.Empty(ds.Completions);
        Assert.Contains(ds.Issues, i => i.Description.Contains("non-existent employee"));
    }

    [Fact]
    public void Same_record_in_two_date_formats_is_counted_once()
    {
        var ds = Build(OneEmployee, completions: "Z1;E1;A;KYBER;16. 1. 2025;;X;0,00;\nZ1;E1;A;KYBER;2025-01-16;;X;0,00;\n");
        Assert.Single(ds.Completions);
    }

    [Fact]
    public void Same_training_on_same_day_under_two_ids_is_counted_once()
    {
        var ds = Build(OneEmployee, completions: "Z1;E1;A;KYBER;2025-01-16;;X;0,00;\nZ2;E1;A;KYBER;2025-01-16;;X;0,00;\n");
        Assert.Single(ds.Completions);
    }

    [Fact]
    public void Future_dated_entry_is_not_counted_as_completed()
    {
        var ds = Build(OneEmployee, completions: "Z1;E1;A;KYBER;2026-11-25;;X;0,00;zadáno předem\n");
        Assert.Empty(ds.Completions);
        Assert.Contains(ds.Issues, i => i.Severity == IssueSeverity.Warning && i.Description.Contains("after the export"));
    }

    [Fact]
    public void Source_validity_disagreeing_with_catalog_is_reported_but_catalog_wins()
    {
        var ds = Build(OneEmployee, completions: "Z1;E1;A;KYBER;2025-01-16;2027-01-16;X;0,00;\n");
        Assert.Contains(ds.Issues, i => i.Description.StartsWith("platnost_do"));
        Assert.Equal(new DateOnly(2027, 1, 16), ds.Completions.Single().SourceValidUntil);
    }

    [Fact]
    public void Multiline_note_with_formula_text_does_not_break_parsing_and_is_not_carried_over()
    {
        var ds = Build(OneEmployee, completions: "Z1;E1;A;KYBER;2025-01-16;;X;0,00;\"řádek 1\n=HYPERLINK(\"\"http://x\"\")\"\nZ2;E1;A;KYBER;2025-02-16;;X;0,00;\n");
        Assert.Equal(2, ds.Completions.Count);
        Assert.DoesNotContain("HYPERLINK", DatasetJsonContext.Serialize(ds));
    }

    [Fact]
    public void Negative_price_is_dropped_and_completion_before_hire_is_flagged_but_kept()
    {
        var ds = Build("E1,A,A,a@x.example,IT,T,,2024-01-01,,aktivní,1,\n", completions: "Z1;E1;A;KYBER;2022-01-16;;X;-600,00;\n");
        Assert.Null(ds.Completions.Single().Price);
        Assert.Contains(ds.Issues, i => i.Description.Contains("before"));
    }

    // ---------- terms ----------

    [Fact]
    public void Invalid_duplicate_cancelled_and_zero_capacity_terms_are_excluded_with_reason()
    {
        var ds = Build(terms:
            "T1;KYBER;2026-11-11;09:00-12:00;Aula A;30;5;800;plánováno\n" +
            "T2;KYBER;2026-11-11;09:00-12:00;Aula A;30;5;800;plánováno\n" +
            "T3;KYBER;31. 9. 2026;09:00-12:00;Aula A;30;0;800;plánováno\n" +
            "T4;KYBER;2026-12-02;09:00-12:00;Aula A;30;0;800;zrušeno\n" +
            "T5;KYBER;2026-12-03;09:00-12:00;Aula A;0;0;800;plánováno\n" +
            "T6;KYBER;2026-12-09;09:00-12:00;Aula A;30;0;800;plánováno\n");
        Assert.Equal(["T6", "T1"], ds.Terms.Where(t => t.Usable).Select(t => t.Id).Reverse());
        Assert.Contains("duplicate", ds.Terms.Single(t => t.Id == "T2").ExcludedReason);
        Assert.Contains("date", ds.Terms.Single(t => t.Id == "T3").ExcludedReason);
        Assert.Equal("cancelled", ds.Terms.Single(t => t.Id == "T4").ExcludedReason);
        Assert.Equal("zero capacity", ds.Terms.Single(t => t.Id == "T5").ExcludedReason);
    }

    [Fact]
    public void Typo_in_term_course_code_is_fixed_and_reported()
    {
        var ds = Build(terms: "T1;KYB3R;2026-12-02;09:00-12:00;Aula B;20;0;800;plánováno\n");
        Assert.Equal("KYBER", ds.Terms.Single().CourseCode);
        Assert.Contains(ds.Issues, i => i.Source == Sources.Terms && i.Ref == "T1");
    }

    [Fact]
    public void Overbooked_term_is_flagged_and_has_no_free_seats()
    {
        var ds = Build(terms: "T1;KYBER;2026-11-04;08:00-11:00;Aula A;30;31;450;plánováno\n");
        Assert.Equal(0, ds.Terms.Single().FreeSeats);
        Assert.Contains(ds.Issues, i => i.Description.Contains("Overbooked"));
    }

    [Fact]
    public void Missing_price_is_estimated_by_median_of_same_course_and_marked()
    {
        var ds = Build(terms:
            "T1;KYBER;2026-11-04;08:00-11:00;A;30;0;800;plánováno\n" +
            "T2;KYBER;2026-11-05;08:00-11:00;A;30;0;1 000 Kč;plánováno\n" +
            "T3;KYBER;2026-11-06;08:00-11:00;A;30;0;;plánováno\n");
        var t3 = ds.Terms.Single(t => t.Id == "T3");
        Assert.True(t3.PriceEstimated);
        Assert.Equal(1000m, t3.Price);
        Assert.Equal(1000m, ds.Terms.Single(t => t.Id == "T2").Price);
        Assert.False(ds.Terms.Single(t => t.Id == "T2").PriceEstimated);
    }

    [Fact]
    public void Weekend_term_is_flagged()
    {
        var ds = Build(terms: "T1;KYBER;2026-11-14;09:00-12:00;Online;60;0;800;plánováno\n");
        Assert.True(ds.Terms.Single().Weekend);
        Assert.Contains(ds.Issues, i => i.Description.Contains("weekend"));
    }

    // ---------- privacy ----------

    [Fact]
    public void Published_json_contains_no_identity_numbers_and_survives_a_round_trip()
    {
        var ds = Build("E1,Jan,Novák,jan@x.example,IT,Technik,,2010-01-01,,aktivní,665917/0359,tajná poznámka\n", completions: "Z1;E1;A;KYBER;2025-01-16;;X;100,00;tajná poznámka k školení\n",
            terms: "T1;KYBER;2026-12-09;09:00-12:00;Aula;30;0;800;plánováno\n");
        var json = DatasetJsonContext.Serialize(ds);
        Assert.DoesNotContain("665917", json);
        Assert.DoesNotContain("tajná", json);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var back = DatasetJsonContext.Deserialize(stream);
        Assert.Equal(ds.Employees, back.Employees);
        Assert.Equal(ds.Terms, back.Terms);
        Assert.Equal(ds.Completions.Count, back.Completions.Count);
    }

    [Fact]
    public void Model_types_have_no_member_that_could_hold_identity_number_or_free_text()
    {
        var forbidden = new[] { "rodne", "birth", "note", "poznamk", "ssn" };
        var members = new[] { typeof(Employee), typeof(Completion), typeof(Term), typeof(Course) }
            .SelectMany(t => t.GetProperties().Select(p => $"{t.Name}.{p.Name}"));
        Assert.DoesNotContain(members, m => forbidden.Any(f => m.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }
}
