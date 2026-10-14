using System.Globalization;
using Skoleni.Core.Domain;
using Skoleni.Core.Models;

namespace Skoleni.Web.Services;

/// <summary>Display formatting. The app runs with InvariantGlobalization (smaller download), so values are formatted by hand.</summary>
public static class Format
{
    public static string Date(DateOnly? d) => d is { } v ? v.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "–";

    public static string Money(decimal value) => "CZK " + value.ToString("#,##0", CultureInfo.InvariantCulture);

    public static string TimeRange(TimeOnly? start, TimeOnly? end) =>
        start is { } s && end is { } e ? $"{s:HH:mm}–{e:HH:mm}" : "–";

    public static string Pct(double share) => $"{Math.Round(share * 100)} %";

    public static string Label(TrainingStatus s) => s switch
    {
        TrainingStatus.Valid => "Valid",
        TrainingStatus.ExpiringSoon => "Expiring soon",
        TrainingStatus.Expired => "Expired",
        _ => "Missing",
    };

    public static string Css(TrainingStatus s) => s switch
    {
        TrainingStatus.Valid => "ok",
        TrainingStatus.ExpiringSoon => "soon",
        TrainingStatus.Expired => "bad",
        _ => "miss",
    };

    public static string Label(EmployeeGroup g) => g switch
    {
        EmployeeGroup.ParentalLeave => "parental leave – not counted in statistics",
        EmployeeGroup.Starting => "starting",
        EmployeeGroup.Terminated => "terminated",
        _ => "",
    };

    public static string Label(IssueSeverity s) => s switch { IssueSeverity.Error => "error", IssueSeverity.Warning => "warning", _ => "info" };
}
