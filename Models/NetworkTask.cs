using GlpiNg.Modules.Abstractions.Entities;
using GlpiNg.Modules.Inventory.Models;

namespace GlpiNg.Modules.Deployment.Models;

/// <summary>Méthode du job réseau — reprend les valeurs "networkdiscovery"/"networkinventory" du
/// sélecteur "Méthode du module" de GLPI-Inventory (front/task.form.php, onglet "Configuration du
/// job"), qui porte aussi deployinstall/collect/InventoryComputerESX — non repris ici, voir
/// <see cref="DeploymentTask"/> pour deployinstall.</summary>
public enum NetworkTaskMethod
{
    NetworkDiscovery,
    NetworkInventory
}

/// <summary>
/// Tâche de scan réseau (découverte ou inventaire SNMP), exécutée par un agent GLPI-Agent réel
/// qui reçoit la spec (plages IP + identifiants SNMP) via le protocole /inventory et remonte ses
/// résultats sous forme de <see cref="DiscoveredNetworkDevice"/> (voir
/// Services/NetworkJobJsonBuilder.cs et AgentController). Entité parallèle à
/// <see cref="DeploymentTask"/> plutôt qu'une extension de celle-ci : DeploymentTask/DeploymentJob
/// sont façonnés autour d'un paquet (PackageId non-nullable, sans discriminant de méthode) sur tout
/// le chemin protocole existant (gate "Deploy" dans AgentController.HandleGet, DeployJobJsonBuilder,
/// ContactAnswer.Jobs) — voir la doc de DeploymentTask, qui documente ce choix initial d'aplatir
/// GLPI-Inventory à la seule méthode deployinstall.
///
/// Contrairement à DeploymentTask (dont les acteurs sont des groupes/ordinateurs, voir
/// DeploymentTaskTarget), les <see cref="Actors"/> d'une NetworkTask référencent directement
/// l'agent GLPI-Agent qui exécute le scan — même pattern que DeploymentJob.AgentId.
/// </summary>
public class NetworkTask : IEntityScoped
{
    /// <inheritdoc />
    public int? EntityId { get; set; }

    /// <inheritdoc />
    public bool IsRecursive { get; set; }

    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Comment { get; set; }
    public bool IsActive { get; set; } = true;
    public NetworkTaskMethod Method { get; set; } = NetworkTaskMethod.NetworkDiscovery;

    public DateTime? ScheduledStartTime { get; set; }
    public DateTime? ScheduledEndTime { get; set; }

    public int? ExecutionTimeSlotId { get; set; }
    public TimeSlot? ExecutionTimeSlot { get; set; }

    /// <summary>Plages IP à scanner — voir <see cref="IpRange"/>.</summary>
    public List<NetworkTaskIpRange> IpRanges { get; set; } = [];

    /// <summary>Identifiants SNMP à essayer sur chaque hôte répondant — pertinent seulement pour
    /// <see cref="NetworkTaskMethod.NetworkInventory"/> (une découverte réseau se contente d'un
    /// ping/ARP, voir <see cref="NetworkTaskMethod.NetworkDiscovery"/>).</summary>
    public List<NetworkTaskCredential> Credentials { get; set; } = [];

    /// <summary>Agents GLPI-Agent chargés d'exécuter le scan — un <see cref="NetworkTaskJob"/> est
    /// créé par acteur au lancement (voir Services/NetworkTaskLaunchService.cs).</summary>
    public List<NetworkTaskActor> Actors { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLaunchedAt { get; set; }

    public List<NetworkTaskJob> Jobs { get; set; } = [];
}

public class NetworkTaskIpRange
{
    public int Id { get; set; }
    public int NetworkTaskId { get; set; }
    public int IpRangeId { get; set; }
    public IpRange? IpRange { get; set; }
}

public class NetworkTaskCredential
{
    public int Id { get; set; }
    public int NetworkTaskId { get; set; }
    public int SnmpCredentialId { get; set; }
    public SnmpCredential? SnmpCredential { get; set; }
}

public class NetworkTaskActor
{
    public int Id { get; set; }
    public int NetworkTaskId { get; set; }
    public int AgentId { get; set; }
    public GlpiAgent? Agent { get; set; }
}
