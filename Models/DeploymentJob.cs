using GlpiNg.Modules.Abstractions.Deployment;
using GlpiNg.Modules.Inventory.Models;

namespace GlpiNg.Modules.Deployment.Models;

public enum DeploymentStatus
{
    Pending,
    Running,
    Success,
    Error
}

/// <summary>
/// Job de déploiement de package pour un agent donné (tâche "deploy" du protocole GLPI-Agent).
/// </summary>
public class DeploymentJob
{
    public int Id { get; set; }

    public int AgentId { get; set; }
    public GlpiAgent? Agent { get; set; }

    public int PackageId { get; set; }
    public DeploymentPackage? Package { get; set; }

    /// <summary>Tâche ayant créé ce job (voir DeploymentTaskLaunchService), nul pour une
    /// assignation directe (self-service ou fiche Ordinateur, voir ComputerDeploymentAssignmentService).</summary>
    public int? TaskId { get; set; }
    public DeploymentTask? Task { get; set; }

    public DeploymentStatus Status { get; set; } = DeploymentStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Journal brut renvoyé par l'agent en fin d'exécution.</summary>
    public string? Log { get; set; }
}

/// <summary>
/// Package de déploiement : fichiers (avec hash SHA512) + actions (cmd, move, copy, delete, mkdir).
/// </summary>
public class DeploymentPackage
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Groupe d'ordinateurs pour lequel le déploiement à la demande (self-service) de ce
    /// paquet est activé — null désactive le self-service (option "-----" du formulaire GLPI-
    /// Inventory d'origine, front/deploypackage.form.php : select plugin_glpiinventory_deploygroups_id).
    /// Distinct de <see cref="Targets"/> : ce champ active/désactive le self-service, les cibles
    /// définissent qui peut le demander (voir leur doc).
    /// </summary>
    public int? DeployComputerGroupId { get; set; }
    public DeployComputerGroup? DeployComputerGroup { get; set; }

    /// <summary>Cibles (entités, groupes, profils, utilisateurs) autorisées à demander ce paquet
    /// en self-service — voir <see cref="DeploymentPackageTarget"/>.</summary>
    public List<DeploymentPackageTarget> Targets { get; set; } = [];

    public List<DeploymentPackageFile> Files { get; set; } = [];

    /// <summary>Liste d'actions sérialisées en JSON (format compatible protocole agent).</summary>
    public string ActionsJson { get; set; } = "[]";

    /// <summary>Liste de vérifications (audits) sérialisées en JSON, évaluées par l'agent avant/pendant le job.</summary>
    public string ChecksJson { get; set; } = "[]";

    /// <summary>Liste d'interactions utilisateur sérialisées en JSON (messages affichés par l'agent).</summary>
    public string UserInteractionsJson { get; set; } = "[]";

    /// <summary>Paquet qui remplace celui-ci. Quand renseigné, ce paquet n'est plus proposé pour le
    /// déploiement à la demande (self-service), même si des cibles y sont configurées.</summary>
    public int? SupersededByPackageId { get; set; }
    public DeploymentPackage? SupersededByPackage { get; set; }
}

/// <summary>
/// Fichier d'un paquet de déploiement, adressé par son SHA-512 (fichier entier). Le contenu
/// n'est jamais stocké comme un blob unique : voir <see cref="Parts"/>, qui reprend le
/// découpage en fragments ("multiparts") du protocole GLPI-Agent réel — moins gourmand pour
/// l'agent (téléchargement/vérification fragment par fragment, reprise possible) et sans
/// doubler l'espace disque ici puisqu'aucune copie "fichier entier" n'est conservée à côté des
/// fragments (voir <c>AgentController.GetDeployFile</c>, qui les reconcatène à la volée).
/// </summary>
public class DeploymentPackageFile
{
    public int Id { get; set; }
    public int DeploymentPackageId { get; set; }
    public required string FileName { get; set; }
    public required string Sha512 { get; set; }
    public long SizeBytes { get; set; }

    public List<DeploymentPackageFilePart> Parts { get; set; } = [];
}

/// <summary>Fragment d'un <see cref="DeploymentPackageFile"/> (voir sa doc). Servi individuellement
/// par <c>GET inventory/deploy/file/part/{sha512}</c>, référencé dans le champ "multiparts" du
/// job JSON construit par <see cref="Services.DeployJobJsonBuilder"/>.</summary>
public class DeploymentPackageFilePart
{
    public int Id { get; set; }
    public int DeploymentPackageFileId { get; set; }

    /// <summary>Position du fragment dans le fichier reconstitué (0-based).</summary>
    public int PartIndex { get; set; }
    public required string Sha512 { get; set; }
    public long SizeBytes { get; set; }
    public required string StoragePath { get; set; }
}

/// <summary>Type de destinataire d'une <see cref="DeploymentPackageTarget"/> (Entité/Groupe/Profil/
/// Utilisateur), même liste que le sélecteur "Ajouter une cible" de GLPI-Inventory
/// (front/deploypackage_item.php).</summary>
public enum DeploymentPackageTargetType
{
    Entity,
    Group,
    Profile,
    User,
}

/// <summary>
/// Cible du déploiement à la demande (self-service) pour un <see cref="DeploymentPackage"/> :
/// une entité, un groupe, un profil ou un utilisateur GLPI autorisé à demander ce paquet.
/// Reprend l'onglet "Cibles pour le déploiement à la demande" de GLPI-Inventory
/// (front/deploypackage_item.php), visible dès que <see cref="DeploymentPackage.DeployComputerGroupId"/>
/// est renseigné (voir Detail.razor.cs).
///
/// Pas de navigation vers l'entité/le groupe/le profil/l'utilisateur ciblé : ces 4 types
/// vivent tous dans <c>GlpiNg.Web.Models</c> (domaine utilisateurs/groupes/entités/profils,
/// pas encore extrait en module), que ce module Deployment ne peut pas référencer sans créer
/// une dépendance circulaire vers l'hôte — comme GLPI lui-même (couple itemtype/items_id
/// sans contrainte FK en base), <see cref="ItemId"/> est une référence polymorphe non
/// contrainte, résolue en affichage via <see cref="IDeploymentTargetDirectory"/>.
/// </summary>
public class DeploymentPackageTarget
{
    public int Id { get; set; }
    public int DeploymentPackageId { get; set; }
    public DeploymentPackageTargetType Type { get; set; }
    public int ItemId { get; set; }
}

