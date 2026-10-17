namespace GlpiNg.Modules.Deployment.Models;

/// <summary>
/// Page « Déploiement automatique » (/tools/deployments/agent-auto) : équipe de l'agent, sans
/// intervention, les postes que la découverte réseau vient de repérer
/// (<see cref="DiscoveredNetworkDevice"/> encore au statut <see cref="DiscoveredDeviceStatus.New"/>).
/// Le fichier d'installation et les options sont ceux d'<see cref="AgentInstallSettings"/> :
/// c'est la même installation, seul le déclenchement change. Exécuté par
/// <c>AgentAutoDeployCronTask</c>.
/// </summary>
public class AgentAutoDeploySettings
{
    public bool Enabled { get; set; }

    /// <summary>Compte administrateur des postes, au format <c>DOMAINE\compte</c> ou UPN.</summary>
    public string AdminUser { get; set; } = string.Empty;

    /// <summary>
    /// Mot de passe du compte ci-dessus, chiffré par <c>AuthSecretProtector</c> — comme le mot de
    /// passe du compte de service d'un annuaire LDAP. Contrairement à la page de déploiement
    /// manuel, qui garde le mot de passe le temps d'un écran, une tâche de fond doit pouvoir le
    /// relire.
    /// </summary>
    public string? AdminPasswordProtected { get; set; }

    /// <summary>
    /// Nombre maximum de postes équipés à chaque passage. Une installation occupe le serveur
    /// plusieurs minutes : sans cette borne, un scan d'un parc entier bloquerait la tâche
    /// pendant des heures.
    /// </summary>
    public int MaxPerRun { get; set; } = 5;

    /// <summary>Au delà, l'équipement n'est plus retenté — voir <see cref="DiscoveredNetworkDevice.AgentDeployAttempts"/>.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// N'équipe que les matériels restés muets en SNMP. Un poste de travail répond rarement au
    /// SNMP, là où une imprimante ou un commutateur se décrivent volontiers : c'est le tri le
    /// moins mauvais pour ne pas s'acharner sur ce qui n'est pas un ordinateur.
    /// </summary>
    public bool SkipSnmpDevices { get; set; } = true;

    /// <summary>
    /// N'équipe que les matériels dont le nom d'hôte contient ce texte. Vide = aucun filtre.
    /// </summary>
    public string? HostnameContains { get; set; }

    /// <summary>
    /// Noms d'hôte et adresses IP à ne jamais équiper, un par ligne.
    /// </summary>
    public string? Exclusions { get; set; }
}
