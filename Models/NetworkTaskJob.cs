using GlpiNg.Modules.Inventory.Models;

namespace GlpiNg.Modules.Deployment.Models;

public enum NetworkJobStatus
{
    Pending,
    Running,
    Success,
    Error
}

/// <summary>
/// Exécution d'une <see cref="NetworkTask"/> par un agent donné (un job par acteur, créé au
/// lancement — voir Services/NetworkTaskLaunchService.cs). La spec (plages IP + identifiants) est
/// lue en direct depuis <see cref="Task"/> au moment de "getNetDiscoveryJobs"/"getNetInventoryJobs"
/// (voir Services/NetworkJobJsonBuilder.cs), pas recopiée ici — même convention que
/// <see cref="DeploymentJob"/>, qui lit <c>Package.Files</c> en direct plutôt que de figer un
/// instantané au lancement.
///
/// <see cref="Id"/> est exposé à l'agent sous forme d'uuid préfixé "net-" (voir
/// AgentController.HandleGetNetworkJobsCoreAsync) pour rester distinguable d'un uuid de
/// <see cref="DeploymentJob"/> (non préfixé) dans "setStatus", les deux étant des entiers qui se
/// chevauchent.
/// </summary>
public class NetworkTaskJob
{
    public int Id { get; set; }

    public int AgentId { get; set; }
    public GlpiAgent? Agent { get; set; }

    public int NetworkTaskId { get; set; }
    public NetworkTask? Task { get; set; }

    public NetworkJobStatus Status { get; set; } = NetworkJobStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Journal brut : lignes "setStatus" reçues de l'agent + éventuel résumé d'ingestion
    /// (nombre d'équipements découverts) — voir Services/NetworkDeviceImportService.cs.</summary>
    public string? Log { get; set; }
}
