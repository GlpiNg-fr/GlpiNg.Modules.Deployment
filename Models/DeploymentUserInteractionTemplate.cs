using GlpiNg.Modules.Abstractions.Entities;

namespace GlpiNg.Modules.Deployment.Models;

/// <summary>
/// Gabarit réutilisable d'interaction utilisateur (message affiché par l'agent pendant un job de
/// déploiement), pour éviter de ressaisir le même texte dans chaque paquet. Un paquet reste libre
/// de ses propres <see cref="DeploymentUserInteractionEntry"/> (voir DeploymentPackageEntries.cs) ;
/// ce gabarit n'en fournit qu'un modèle de départ, il n'y est pas rattaché en base.
/// </summary>
public class DeploymentUserInteractionTemplate : IEntityScoped
{
    /// <inheritdoc />
    public int? EntityId { get; set; }

    /// <inheritdoc />
    public bool IsRecursive { get; set; }

    public int Id { get; set; }
    public required string Name { get; set; }
    public DeploymentUserInteractionType Type { get; set; } = DeploymentUserInteractionType.InfoMessage;
    public string Text { get; set; } = string.Empty;
    public bool AllowSkip { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
