namespace GlpiNg.Modules.Deployment.Models;

/// <summary>
/// Onglet "Général &gt; Configuration générale" du module de déploiement (voir
/// Deployments/GeneralConfig.razor), calqué sur front/config.form.php de GLPI-Inventory.
/// Les réglages "SSL seulement pour l'agent" et "Port de l'agent" de GLPI n'ont pas
/// d'équivalent ici : GlpiNg n'expose qu'un seul endpoint /glpi-agent sur l'écoute Kestrel
/// déjà configurée depuis /config, donc ces deux champs sont volontairement omis.
/// </summary>
public class DeploymentGeneralSettings
{
    /// <summary>Purge les <c>DeploymentJob</c> plus anciens que N jours (pas encore de tâche cron dédiée).</summary>
    public int TaskLogRetentionDays { get; set; } = 20;

    public bool RepareSuccessfulJobs { get; set; }

    /// <summary>Non branché : GlpiNg n'a pas de mécanisme de réveil d'agent (Wake-on-LAN).</summary>
    public int MaxAgentsToWakePerTask { get; set; } = 10;

    public bool ExtraDebug { get; set; }
}
