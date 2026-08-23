namespace GlpiNg.Modules.Deployment.Models;

/// <summary>
/// Onglets "Configuration générale" et "Gestion de paquets" du module de déploiement (voir
/// Deployments/GeneralConfig.razor), calqués sur front/config.form.php de GLPI-Inventory.
/// Les réglages "SSL seulement pour l'agent" et "Port de l'agent" de GLPI n'ont pas
/// d'équivalent ici : GlpiNg n'expose qu'un seul endpoint /glpi-agent sur l'écoute Kestrel
/// déjà configurée depuis /config, donc ces deux champs sont volontairement omis. Le champ
/// "Supprimer les tâches à la demande réussies après" n'a pas non plus d'équivalent : GlpiNg ne
/// distingue pas les tâches "à la demande" des tâches planifiées (<see cref="TaskLogRetentionDays"/>
/// couvre déjà la purge de l'historique, quel que soit le mode de lancement).
/// </summary>
public class DeploymentGeneralSettings
{
    /// <summary>Purge les <c>DeploymentJob</c> plus anciens que N jours (pas encore de tâche cron dédiée).</summary>
    public int TaskLogRetentionDays { get; set; } = 20;

    public bool RepareSuccessfulJobs { get; set; }

    /// <summary>Non branché : GlpiNg n'a pas de mécanisme de réveil d'agent (Wake-on-LAN).</summary>
    public int MaxAgentsToWakePerTask { get; set; } = 10;

    public bool ExtraDebug { get; set; }

    /// <summary>Le serveur GlpiNg central sert aussi les fichiers de paquet directement, en plus
    /// des <see cref="DeploymentMirrorServer"/> déclarés ("server_as_mirror" côté GLPI-Inventory).
    /// Non branché : voir la remarque sur <see cref="DeploymentMirrorServer"/> — pour l'instant
    /// <c>DeployJobJsonBuilder</c> sert toujours depuis le serveur central quoi qu'il arrive.</summary>
    public bool UseCentralServerAsMirror { get; set; } = true;

    /// <summary>Critère utilisé pour choisir le serveur miroir le plus proche d'un agent
    /// ("mirror_match" côté GLPI-Inventory) — capturé pour la parité de formulaire, non consommé
    /// (voir la remarque sur <see cref="DeploymentMirrorServer"/>).</summary>
    public DeploymentMirrorMatchMode MirrorMatchMode { get; set; } = DeploymentMirrorMatchMode.Location;
}
