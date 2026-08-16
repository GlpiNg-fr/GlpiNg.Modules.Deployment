namespace GlpiNg.Modules.Deployment.Models;

/// <summary>
/// Serveur miroir (P2P) : point de distribution alternatif où les agents peuvent récupérer les
/// fichiers d'un paquet de déploiement, pour répartir la bande passante entre sites plutôt que de
/// tout servir depuis le serveur GlpiNg central. Pas encore consommé par <c>DeployJobJsonBuilder</c> :
/// ce référentiel existe pour la gestion des serveurs miroirs eux-mêmes.
/// </summary>
public class DeploymentMirrorServer
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Url { get; set; }
    public string? Comment { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
