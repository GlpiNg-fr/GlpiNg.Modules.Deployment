using GlpiNg.Modules.Abstractions.Entities;

namespace GlpiNg.Modules.Deployment.Models;

/// <summary>
/// Créneau horaire nommé (ex: "Lun-Ven : 9h-17h"), utilisé comme référentiel pour les
/// fenêtres de préparation/exécution des tâches planifiées côté GLPI-Inventory
/// (front/timeslot.php). GlpiNg n'a pas encore de moteur de planification de tâches
/// (voir DeploymentJob) : ce référentiel existe pour la gestion des créneaux eux-mêmes,
/// pas encore rattaché à un ordonnanceur.
/// </summary>
public class TimeSlot : IEntityScoped
{
    /// <inheritdoc />
    public int? EntityId { get; set; }

    /// <inheritdoc />
    public bool IsRecursive { get; set; }

    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Comment { get; set; }

    public List<TimeSlotEntry> Entries { get; set; } = [];
}

public class TimeSlotEntry
{
    public int Id { get; set; }
    public int TimeSlotId { get; set; }

    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}
