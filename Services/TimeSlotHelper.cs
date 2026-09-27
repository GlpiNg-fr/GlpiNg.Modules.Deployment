using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Abstractions.Localization;

namespace GlpiNg.Modules.Deployment.Services;

/// <summary>
/// Aperçu textuel d'un <see cref="TimeSlot"/> (bouton "i" à côté des selects de créneau horaire).
/// Extrait en classe statique partagée depuis TaskDetail.razor.cs (seul consommateur jusqu'ici)
/// pour être réutilisé par NetworkTaskDetail.razor.cs plutôt que dupliqué.
/// </summary>
public static class TimeSlotHelper
{
    public static string Tooltip(IReadOnlyCollection<TimeSlot> availableTimeSlots, int? timeSlotId)
    {
        if (timeSlotId is not { } id)
        {
            return Tr.T("Aucun créneau sélectionné.");
        }

        TimeSlot? slot = availableTimeSlots.FirstOrDefault(s => s.Id == id);
        if (slot is null || slot.Entries.Count == 0)
        {
            return Tr.T("Ce créneau n'a aucune entrée configurée.");
        }

        return string.Join(", ", slot.Entries
            .OrderBy(e => e.DayOfWeek)
            .ThenBy(e => e.StartTime)
            .Select(e => $"{DayLabel(e.DayOfWeek)} {e.StartTime:HH:mm}-{e.EndTime:HH:mm}"));
    }

    public static string DayLabel(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => Tr.T("Lundi"),
        DayOfWeek.Tuesday => Tr.T("Mardi"),
        DayOfWeek.Wednesday => Tr.T("Mercredi"),
        DayOfWeek.Thursday => Tr.T("Jeudi"),
        DayOfWeek.Friday => Tr.T("Vendredi"),
        DayOfWeek.Saturday => Tr.T("Samedi"),
        DayOfWeek.Sunday => Tr.T("Dimanche"),
        _ => day.ToString()
    };
}
