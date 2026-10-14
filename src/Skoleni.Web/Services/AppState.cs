using System.Net.Http.Json;
using Skoleni.Core.Domain;
using Skoleni.Core.Models;

namespace Skoleni.Web.Services;

public enum Tab { Overview, People, Plan, Terms, Quality }

public sealed class PeopleFilter
{
    public string Query { get; set; } = "";
    public string Department { get; set; } = "";
    public string ManagerId { get; set; } = "";
    public string CourseCode { get; set; } = "";
    /// <summary>"problem" = everything except valid, "all" = everybody, otherwise a TrainingStatus name.</summary>
    public string Status { get; set; } = "problem";
}

/// <summary>
/// The single place holding application state: the dataset, the chosen date and the "expiring soon" threshold, and the
/// requirements and plan derived from them. All domain logic lives in Skoleni.Core; this class only calls it and keeps the result.
/// </summary>
public sealed class AppState(HttpClient http)
{
    public Dataset Data { get; private set; } = null!;
    public bool Loaded => Data is not null;
    public string? Error { get; private set; }

    public DateOnly AsOf { get; private set; }
    public int SoonDays { get; private set; } = 90;
    public IReadOnlyList<Requirement> Requirements { get; private set; } = [];
    public Plan Plan { get; private set; } = null!;
    public IReadOnlyDictionary<string, Employee> EmployeeById { get; private set; } = new Dictionary<string, Employee>();
    public IReadOnlyDictionary<string, Course> CourseByCode { get; private set; } = new Dictionary<string, Course>();

    public Tab Tab { get; private set; } = Tab.Overview;
    public PeopleFilter Filter { get; private set; } = new();

    public event Action? Changed;

    public async Task LoadAsync()
    {
        if (Loaded) return;
        try
        {
            await using var stream = await http.GetStreamAsync("data/dataset.json");
            Data = await DatasetJsonContext.DeserializeAsync(stream);
            EmployeeById = Data.Employees.ToDictionary(e => e.Id);
            CourseByCode = Data.Courses.ToDictionary(c => c.Code);
            AsOf = Data.ExportDate;
            Recompute();
        }
        catch (Exception ex)
        {
            Error = $"Failed to load the data: {ex.Message}";
        }
        Changed?.Invoke();
    }

    public void SetAsOf(DateOnly date) { AsOf = date; Recompute(); Changed?.Invoke(); }
    public void SetSoonDays(int days) { SoonDays = days; Recompute(); Changed?.Invoke(); }

    public void Go(Tab tab, PeopleFilter? filter = null)
    {
        Tab = tab;
        if (filter is not null) Filter = filter;
        Changed?.Invoke();
    }

    public string ManagerName(Employee e) => e.ManagerId is not null && EmployeeById.TryGetValue(e.ManagerId, out var m) ? m.FullName : "";

    private void Recompute()
    {
        Requirements = Compliance.Compute(Data, AsOf, SoonDays);
        Plan = Planner.Build(Data, Requirements, AsOf);
    }
}
