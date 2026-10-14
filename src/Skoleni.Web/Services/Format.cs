using Skoleni.Core.Domain;
using Skoleni.Core.Models;

namespace Skoleni.Web.Services;

/// <summary>Zobrazení hodnot. Aplikace běží s InvariantGlobalization (menší stažení), proto formátujeme ručně.</summary>
public static class Format
{
    public static string Date(DateOnly? d) => d is { } v ? $"{v.Day}. {v.Month}. {v.Year}" : "–";

    public static string Kc(decimal value) => value.ToString("#,##0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ') + " Kč";

    public static string Pct(double share) => $"{Math.Round(share * 100)} %";

    public static string Label(TrainingStatus s) => s switch
    {
        TrainingStatus.Valid => "Platné",
        TrainingStatus.ExpiringSoon => "Brzy vyprší",
        TrainingStatus.Expired => "Prošlé",
        _ => "Chybí",
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
        EmployeeGroup.ParentalLeave => "rodičovská – do statistik se nezapočítává",
        EmployeeGroup.Starting => "nastupuje",
        EmployeeGroup.Terminated => "ukončen",
        _ => "",
    };

    public static string Label(IssueSeverity s) => s switch { IssueSeverity.Error => "chyba", IssueSeverity.Warning => "varování", _ => "info" };
}
