namespace GlpiNg.Modules.Deployment.Models;

/// <summary>
/// Page « Installation de l'agent » (/tools/deployments/agent-install) : le fichier
/// d'installation de GLPI-Agent téléversé sur le serveur, et les options passées à
/// l'installeur sur chaque poste distant. Stocké comme section de la table AppSettings,
/// comme <see cref="DeploymentGeneralSettings"/>.
///
/// Les noms des propriétés MSI (SERVER, TAG, RUNNOW...) sont ceux de l'installeur Windows
/// officiel de GLPI-Agent : https://glpi-agent.readthedocs.io/en/latest/installation/windows-command-line.html
/// </summary>
public class AgentInstallSettings
{
    /// <summary>Nom du fichier téléversé, rangé sous <c>{Storage:RootPath}/agent-installers/</c>. Null tant qu'aucun n'a été téléversé.</summary>
    public string? InstallerFileName { get; set; }

    public long InstallerSizeBytes { get; set; }

    /// <summary>
    /// Jeton de l'URL de téléchargement de l'installeur (<c>/inventory/agent-installer/{token}</c>).
    /// Cette URL est forcément anonyme — le poste distant n'a pas encore d'agent, donc pas de
    /// session —, le jeton est donc tout ce qui la protège. Régénéré à chaque téléversement.
    /// </summary>
    public string DownloadToken { get; set; } = string.Empty;

    /// <summary>URL complète du point d'entrée de ce serveur pour les agents (propriété MSI <c>SERVER</c>).</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>Propriété MSI <c>TAG</c> — marque reprise dans l'inventaire remonté.</summary>
    public string? Tag { get; set; }

    /// <summary>Propriété MSI <c>RUNNOW</c> : lance un inventaire dès la fin de l'installation.</summary>
    public bool RunInventoryNow { get; set; } = true;

    /// <summary>Propriété MSI <c>NO_SSL_CHECK</c> : utile quand le serveur porte un certificat auto-signé.</summary>
    public bool NoSslCheck { get; set; }

    /// <summary>Propriété MSI <c>NO_HTTPD</c> : désactive le petit serveur HTTP local de l'agent.</summary>
    public bool NoHttpd { get; set; }

    /// <summary>Propriété MSI <c>HTTPD_PORT</c>, ignorée si <see cref="NoHttpd"/>.</summary>
    public int HttpdPort { get; set; } = 62354;

    /// <summary>Propriété MSI <c>INSTALLDIR</c> ; vide = répertoire par défaut de l'installeur.</summary>
    public string? InstallDir { get; set; }

    /// <summary>
    /// Toutes les autres propriétés de l'installeur, indexées par nom MSI — voir
    /// <see cref="AgentInstallProperties"/>, qui décrit celles que le formulaire propose. Seules
    /// celles qui s'écartent de la valeur par défaut de l'installeur sont passées en ligne de
    /// commande, pour qu'une commande reste lisible.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = [];

    /// <summary>Port WinRM des postes cibles (5985 en HTTP, 5986 en HTTPS).</summary>
    public int WinRmPort { get; set; } = 5985;

    public bool WinRmUseHttps { get; set; }

    /// <summary>Accepte un certificat WinRM non vérifiable (auto-signé), fréquent en HTTPS sans PKI.</summary>
    public bool WinRmAcceptAnyCertificate { get; set; }

    /// <summary>Délai maximum accordé à l'installation sur un poste.</summary>
    public int TimeoutMinutes { get; set; } = 15;
}
