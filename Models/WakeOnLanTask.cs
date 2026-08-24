using GlpiNg.Modules.Inventory.Models;

namespace GlpiNg.Modules.Deployment.Models;

/// <summary>Statut d'un <see cref="WakeOnLanTaskJob"/> — copie structurelle de
/// <see cref="NetworkJobStatus"/>/<see cref="DeploymentStatus"/> (voir la remarque sur
/// <see cref="WakeOnLanTaskJob"/> : chaque stack de méthode a son propre enum, pas de type
/// partagé dans ce module).</summary>
public enum WakeOnLanJobStatus
{
    Pending,
    Running,
    Success,
    Error
}

/// <summary>Type d'acteur d'un <see cref="WakeOnLanTaskTarget"/> — copie de
/// <see cref="DeploymentTaskTargetType"/> (même choix de duplication qu'ailleurs dans ce
/// module plutôt que de réutiliser le type de DeploymentTask).</summary>
public enum WakeOnLanTaskTargetType
{
    Group,
    Computer
}

/// <summary>
/// Tâche de réveil réseau ("wakeonlan" dans le sélecteur "Méthode du module" de GLPI-Inventory,
/// front/task.form.php) : réveille un ensemble d'ordinateurs cibles (<see cref="Targets"/>, même
/// modèle groupe/ordinateur que <see cref="DeploymentTask.Targets"/> — car contrairement à
/// <see cref="NetworkTask"/>, la cible finale n'est pas l'agent qui exécute le job mais
/// l'ordinateur à réveiller) en envoyant un magic packet depuis un ou plusieurs agents relais
/// (<see cref="RelayAgents"/>, même modèle que <see cref="NetworkTask.Actors"/> : ces agents
/// doivent être sur le même sous-réseau que les cibles pour que le broadcast atteigne les
/// machines éteintes). <see cref="Services.WakeOnLanTaskLaunchService"/> résout les
/// <see cref="Targets"/> en ordinateurs puis en adresses MAC (via
/// <see cref="Computer.NetworkPorts"/>, seule source de MAC — <see cref="GlpiAgent"/> n'en porte
/// pas), un <see cref="WakeOnLanTaskJob"/> par agent relais.
/// </summary>
public class WakeOnLanTask
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Comment { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime? ScheduledStartTime { get; set; }
    public DateTime? ScheduledEndTime { get; set; }

    public int? ExecutionTimeSlotId { get; set; }
    public TimeSlot? ExecutionTimeSlot { get; set; }

    /// <summary>Ordinateurs/groupes à réveiller.</summary>
    public List<WakeOnLanTaskTarget> Targets { get; set; } = [];

    /// <summary>Agents GLPI-Agent chargés d'émettre le magic packet.</summary>
    public List<WakeOnLanTaskActor> RelayAgents { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLaunchedAt { get; set; }

    public List<WakeOnLanTaskJob> Jobs { get; set; } = [];
}

/// <summary>Cible réveillée par une <see cref="WakeOnLanTask"/> — copie exacte de la forme de
/// <see cref="DeploymentTaskTarget"/> (exactement une des deux références renseignée selon
/// <see cref="Type"/>).</summary>
public class WakeOnLanTaskTarget
{
    public int Id { get; set; }
    public int WakeOnLanTaskId { get; set; }
    public WakeOnLanTaskTargetType Type { get; set; }

    public int? GroupId { get; set; }
    public DeployComputerGroup? Group { get; set; }

    public int? ComputerId { get; set; }
    public Computer? Computer { get; set; }
}

/// <summary>Agent relais d'une <see cref="WakeOnLanTask"/> — copie exacte de
/// <see cref="NetworkTaskActor"/>.</summary>
public class WakeOnLanTaskActor
{
    public int Id { get; set; }
    public int WakeOnLanTaskId { get; set; }
    public int AgentId { get; set; }
    public GlpiAgent? Agent { get; set; }
}

/// <summary>
/// Exécution d'une <see cref="WakeOnLanTask"/> par un agent relais donné (un job par agent, créé
/// au lancement — voir Services/WakeOnLanTaskLaunchService.cs). Contrairement à
/// <see cref="NetworkTaskJob"/> (qui lit sa spec en direct depuis sa tâche), les cibles à réveiller
/// sont figées dans <see cref="TargetMacsJson"/> au moment du lancement : les
/// <see cref="WakeOnLanTask.Targets"/> (groupes dynamiques notamment) peuvent changer entre le
/// lancement et l'exécution effective par l'agent, et on veut conserver un historique fiable de
/// qui a réellement été réveillé par ce job plutôt qu'une résolution qui dérive dans le temps.
/// </summary>
public class WakeOnLanTaskJob
{
    public int Id { get; set; }

    public int AgentId { get; set; }
    public GlpiAgent? Agent { get; set; }

    public int WakeOnLanTaskId { get; set; }
    public WakeOnLanTask? Task { get; set; }

    public WakeOnLanJobStatus Status { get; set; } = WakeOnLanJobStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Instantané JSON des cibles résolues au lancement : <c>[{"computerId":12,"mac":"AA:BB:CC:DD:EE:FF"}]</c>
    /// — voir la doc de la classe.</summary>
    public required string TargetMacsJson { get; set; }

    /// <summary>Journal brut des lignes "setStatus" reçues de l'agent relais.</summary>
    public string? Log { get; set; }
}
