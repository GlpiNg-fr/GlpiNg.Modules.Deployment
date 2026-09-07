using GlpiNg.Modules.Abstractions.Entities;

namespace GlpiNg.Modules.Deployment.Models;

/// <summary>Critère de correspondance entre un agent et un serveur miroir (onglet "Gestion de
/// paquets" de front/config.form.php côté GLPI-Inventory, champ "mirror_match") — pas encore
/// consommé, voir la remarque sur <see cref="DeploymentMirrorServer"/>.</summary>
public enum DeploymentMirrorMatchMode
{
    Location,
    Entity,
    Both
}

/// <summary>
/// Serveur miroir (P2P) : point de distribution alternatif où les agents peuvent récupérer les
/// fichiers d'un paquet de déploiement, pour répartir la bande passante entre sites plutôt que de
/// tout servir depuis le serveur GlpiNg central. Pas encore consommé par <c>DeployJobJsonBuilder</c> :
/// ce référentiel existe pour la gestion des serveurs miroirs eux-mêmes.
/// </summary>
public class DeploymentMirrorServer : IEntityScoped
{
    /// <inheritdoc />
    public int? EntityId { get; set; }

    /// <inheritdoc />
    public bool IsRecursive { get; set; }

    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Url { get; set; }
    public string? Comment { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
