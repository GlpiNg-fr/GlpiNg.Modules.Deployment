using GlpiNg.Modules.Abstractions.Entities;
using GlpiNg.Modules.Inventory.Models;

namespace GlpiNg.Modules.Deployment.Models;

/// <summary>
/// Tâche de déploiement : associe un ou plusieurs <see cref="DeploymentPackage"/> (voir
/// <see cref="Packages"/>) à un ou plusieurs acteurs (voir <see cref="Targets"/>) et peut être
/// lancée ("Lancer maintenant") pour créer un <see cref="DeploymentJob"/> par (agent × paquet)
/// résolu à partir de ces acteurs — voir <see cref="Services.DeploymentTaskLaunchService"/>.
/// Reprend l'onglet "Gestion des tâches"/"Configuration du job" de GLPI-Inventory
/// (front/task.form.php), simplifié à la seule méthode "deploy" que GlpiNg reprend (voir la
/// remarque sur <see cref="Services.ComputerDeploymentTasksProvider"/>) : pas de
/// netdiscovery/netinventory/esx/wakeonlan, ni de moteur de planification récurrente (voir
/// <see cref="TimeSlot"/>) — le lancement est toujours manuel, déclenché depuis l'UI. Contrairement
/// à GLPI-Inventory, qui sépare la tâche (planification) de son "job" (méthode + acteurs), GlpiNg
/// aplatit les deux en une seule entité : <see cref="Packages"/> et <see cref="Targets"/> sont
/// vides juste après la création (voir la modale "Nouvelle tâche", qui ne demande que
/// <see cref="Name"/>/<see cref="Comment"/>/<see cref="AllowRePreparation"/>) et se peuplent
/// ensuite sur la fiche de la tâche — la tâche n'est lançable qu'une fois les deux non vides.
/// </summary>
public class DeploymentTask : IEntityScoped
{
    /// <inheritdoc />
    public int? EntityId { get; set; }

    /// <inheritdoc />
    public bool IsRecursive { get; set; }

    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Comment { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Si faux (par défaut), une tâche dont tous les jobs sont dans un état terminal
    /// (Success/Error) ne peut plus être relancée — reprend le champ "Permet la re-préparation de
    /// la tâche après son exécution" de GLPI-Inventory (front/task.form.php), qui évite de
    /// re-déployer par erreur un paquet déjà appliqué à une cible déjà traitée. Voir
    /// DeploymentTaskLaunchService.LaunchAsync pour la vérification correspondante.</summary>
    public bool AllowRePreparation { get; set; }

    /// <summary>Fenêtre planifiée (début/fin) reprise de GLPI-Inventory (front/task.form.php) —
    /// comme <see cref="PreparationTimeSlotId"/>/<see cref="ExecutionTimeSlotId"/> et les champs de
    /// réveil des agents ci-dessous, purement déclaratif pour l'instant : GlpiNg n'a pas encore de
    /// moteur de planification récurrente (voir <see cref="TimeSlot"/>) qui les consommerait — le
    /// lancement reste toujours manuel ("Lancer maintenant").</summary>
    public DateTime? ScheduledStartTime { get; set; }
    public DateTime? ScheduledEndTime { get; set; }

    /// <summary>Créneau horaire de préparation (téléchargement des paquets sur les agents avant
    /// exécution côté GLPI-Inventory) — voir la remarque sur <see cref="ScheduledStartTime"/>.</summary>
    public int? PreparationTimeSlotId { get; set; }
    public TimeSlot? PreparationTimeSlot { get; set; }

    /// <summary>Créneau horaire d'exécution effective du job — voir la remarque sur
    /// <see cref="ScheduledStartTime"/>.</summary>
    public int? ExecutionTimeSlotId { get; set; }
    public TimeSlot? ExecutionTimeSlot { get; set; }

    /// <summary>Intervalle (en minutes) entre deux vagues de réveil Wake-on-LAN, 0 = "Jamais" —
    /// GlpiNg ne sait pas encore émettre de paquet magique WoL (voir la remarque sur
    /// <see cref="ScheduledStartTime"/>), ce champ est capturé pour la parité de formulaire avec
    /// GLPI-Inventory (front/task.form.php) uniquement.</summary>
    public int AgentWakeUpIntervalMinutes { get; set; }

    /// <summary>Nombre d'agents réveillés par vague, 0 = "Aucun" — voir
    /// <see cref="AgentWakeUpIntervalMinutes"/>.</summary>
    public int AgentWakeUpCount { get; set; }

    /// <summary>Paquets à déployer par cette tâche — GLPI-Inventory permet d'associer plusieurs
    /// paquets à un même job de déploiement, exécutés pour chaque acteur ciblé (voir
    /// <see cref="Targets"/>).</summary>
    public List<DeploymentTaskPackage> Packages { get; set; } = [];

    /// <summary>Acteurs (groupes d'ordinateurs statiques/dynamiques ou ordinateurs individuels)
    /// ciblés par cette tâche — GLPI-Inventory permet d'en combiner plusieurs de types différents
    /// sur un même job (voir <see cref="DeploymentTaskTarget"/>).</summary>
    public List<DeploymentTaskTarget> Targets { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Date du dernier lancement (manuel), qu'il ait produit des jobs ou non — voir
    /// DeploymentTaskLaunchService.LaunchAsync.</summary>
    public DateTime? LastLaunchedAt { get; set; }

    /// <summary>Jobs de déploiement créés par les lancements de cette tâche (historique
    /// d'exécution, onglet "Exécutions des jobs" de TaskDetail).</summary>
    public List<DeploymentJob> Jobs { get; set; } = [];
}

/// <summary>Association many-to-many entre une <see cref="DeploymentTask"/> et un
/// <see cref="DeploymentPackage"/> à déployer (voir <see cref="DeploymentTask.Packages"/>).</summary>
public class DeploymentTaskPackage
{
    public int Id { get; set; }
    public int DeploymentTaskId { get; set; }
    public int PackageId { get; set; }
    public DeploymentPackage? Package { get; set; }
}

/// <summary>Type d'acteur d'une <see cref="DeploymentTaskTarget"/> — un groupe d'ordinateurs
/// (statique ou dynamique, voir <see cref="DeployComputerGroup.Type"/>, déjà unifiés dans
/// GlpiNg contrairement à GLPI-Inventory qui distingue "Groupe" et "Groupe dynamique") ou un
/// ordinateur individuel.</summary>
public enum DeploymentTaskTargetType
{
    Group,
    Computer
}

/// <summary>Acteur ciblé par le job de déploiement d'une <see cref="DeploymentTask"/> — reprend
/// les "acteurs" de la configuration du job de GLPI-Inventory (front/task.form.php, onglet
/// "Configuration du job"), qui autorise plusieurs cibles hétérogènes par job. Exactement une des
/// deux références (<see cref="Group"/> ou <see cref="Computer"/>) est renseignée, selon
/// <see cref="Type"/>.</summary>
public class DeploymentTaskTarget
{
    public int Id { get; set; }
    public int DeploymentTaskId { get; set; }
    public DeploymentTaskTargetType Type { get; set; }

    public int? GroupId { get; set; }
    public DeployComputerGroup? Group { get; set; }

    public int? ComputerId { get; set; }
    public Computer? Computer { get; set; }
}
