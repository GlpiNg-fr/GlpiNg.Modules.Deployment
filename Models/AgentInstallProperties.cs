namespace GlpiNg.Modules.Deployment.Models;

/// <summary>Nature du champ présenté pour une propriété de l'installeur.</summary>
public enum AgentInstallPropertyKind
{
    /// <summary>Propriété 0/1, rendue par un interrupteur.</summary>
    Switch,

    /// <summary>Texte libre.</summary>
    Text,

    /// <summary>Entier, rendu par un champ numérique.</summary>
    Number,

    /// <summary>Texte masqué à la saisie (mot de passe, secret).</summary>
    Secret,

    /// <summary>Liste fermée de valeurs, rendue par un menu déroulant.</summary>
    Choice,
}

/// <summary>
/// Une propriété de l'installeur Windows de GLPI-Agent telle que le formulaire la présente.
/// <paramref name="Help"/> devient l'infobulle du champ — c'est là que se lisent les valeurs
/// acceptées quand elles ne tiennent pas dans une liste fermée (catégories, tâches, adresses…).
/// </summary>
/// <param name="Name">Nom de la propriété MSI, tel quel (<c>DEBUG</c>, <c>NO_P2P</c>…).</param>
/// <param name="Kind">Champ à présenter.</param>
/// <param name="Label">Intitulé affiché.</param>
/// <param name="Help">Infobulle : ce que fait la propriété et ce qu'elle accepte.</param>
/// <param name="Default">Valeur par défaut de l'installeur. Une valeur identique n'est pas passée en ligne de commande.</param>
/// <param name="Choices">Couples valeur/intitulé, pour <see cref="AgentInstallPropertyKind.Choice"/> uniquement.</param>
public sealed record AgentInstallProperty(
    string Name,
    AgentInstallPropertyKind Kind,
    string Label,
    string Help,
    string Default = "",
    (string Value, string Label)[]? Choices = null);

/// <summary>
/// Catalogue des propriétés de l'installeur Windows de GLPI-Agent, groupées comme le formulaire
/// les affiche (voir Components/Pages/Deployments/AgentInstall.razor).
/// Source : https://glpi-agent.readthedocs.io/en/latest/installation/windows-command-line.html
///
/// Les sept propriétés les plus courantes (SERVER, TAG, RUNNOW, NO_SSL_CHECK, NO_HTTPD,
/// HTTPD_PORT, INSTALLDIR) n'y figurent pas : elles ont leur propre champ en haut du formulaire,
/// dans <see cref="AgentInstallSettings"/>.
///
/// Textes en français, jamais traduits ici : un catalogue statique figerait la langue du premier
/// lecteur (voir la règle i18n du CLAUDE.md). La page les passe à <c>T(...)</c> à l'affichage.
/// </summary>
public static class AgentInstallProperties
{
    /// <summary>Les propriétés, par groupe d'affichage et dans l'ordre du formulaire.</summary>
    public static readonly IReadOnlyList<(string Group, IReadOnlyList<AgentInstallProperty> Properties)> Groups =
    [
        ("Exécution",
        [
            new("EXECMODE", AgentInstallPropertyKind.Choice, "Mode d'exécution",
                "Comment l'agent tourne une fois installé.", "1",
                [("1", "Service"), ("2", "Tâche planifiée"), ("3", "Manuel")]),
            new("DELAYTIME", AgentInstallPropertyKind.Number, "Délai initial aléatoire (s)",
                "Délai maximum tiré au hasard avant le premier contact avec le serveur. Mode service uniquement.", "3600"),
            new("LAZY", AgentInstallPropertyKind.Switch, "Ne contacter le serveur qu'à expiration du délai",
                "Mode tâche planifiée uniquement.", "1"),
            new("TASK_FREQUENCY", AgentInstallPropertyKind.Choice, "Fréquence de la tâche planifiée",
                "Mode tâche planifiée uniquement.", "hourly",
                [("minute", "Toutes les N minutes"), ("hourly", "Toutes les N heures"), ("daily", "Tous les N jours")]),
            new("TASK_MINUTE_MODIFIER", AgentInstallPropertyKind.Number, "Intervalle en minutes",
                "De 1 à 1439. Utilisé quand la fréquence est « toutes les N minutes ».", "15"),
            new("TASK_HOURLY_MODIFIER", AgentInstallPropertyKind.Number, "Intervalle en heures",
                "De 1 à 23. Utilisé quand la fréquence est « toutes les N heures ».", "1"),
            new("TASK_DAILY_MODIFIER", AgentInstallPropertyKind.Number, "Intervalle en jours",
                "De 1 à 365. Utilisé quand la fréquence est « tous les N jours ».", "1"),
            new("BACKEND_COLLECT_TIMEOUT", AgentInstallPropertyKind.Number, "Délai d'un module d'inventaire (s)",
                "Temps accordé à un module de collecte avant abandon.", "180"),
        ]),

        ("Serveur",
        [
            new("USER", AgentInstallPropertyKind.Text, "Identifiant d'authentification",
                "Compte d'authentification HTTP auprès du serveur. Sans effet si aucun serveur n'est renseigné."),
            new("PASSWORD", AgentInstallPropertyKind.Secret, "Mot de passe d'authentification",
                "Enregistré tel quel dans la configuration de GlpiNg et visible dans le script d'installation : à réserver à un compte dédié."),
            new("PROXY", AgentInstallPropertyKind.Text, "Serveur mandataire (proxy)",
                "URI du proxy HTTP/HTTPS, par exemple http://proxy.exemple.fr:3128."),
            new("TIMEOUT", AgentInstallPropertyKind.Number, "Délai de connexion au serveur (s)",
                "Sans effet si aucun serveur n'est renseigné.", "180"),
            new("NO_COMPRESSION", AgentInstallPropertyKind.Switch, "Désactiver la compression",
                "Les échanges avec le serveur ne sont plus compressés.", "0"),
            new("OAUTH_CLIENT_ID", AgentInstallPropertyKind.Text, "Identifiant client OAuth2",
                "Plusieurs valeurs séparées par des virgules sont acceptées."),
            new("OAUTH_CLIENT_SECRET", AgentInstallPropertyKind.Secret, "Secret client OAuth2",
                "Même remarque que le mot de passe : enregistré tel quel, et visible dans le script d'installation."),
            new("GLPI_VERSION", AgentInstallPropertyKind.Text, "Version de GLPI attendue",
                "Active les fonctions dépendantes de la version du serveur, par exemple 10.0.12."),
        ]),

        ("Certificats SSL",
        [
            new("CA_CERT_FILE", AgentInstallPropertyKind.Text, "Fichier d'autorités de certification",
                "Chemin d'un fichier PEM contenant une ou plusieurs autorités. Exclusif du dossier ci-dessous."),
            new("CA_CERT_DIR", AgentInstallPropertyKind.Text, "Dossier d'autorités de certification",
                "Dossier de certificats PEM nommés par empreinte de sujet, extension .0. Exclusif du fichier ci-dessus."),
            new("SSL_CERT_FILE", AgentInstallPropertyKind.Text, "Certificat client",
                "Certificat présenté au serveur, ou utilisé pour l'inventaire distant WinRM."),
            new("SSL_KEY_FILE", AgentInstallPropertyKind.Text, "Clé privée du certificat client",
                "Clé privée associée au certificat client."),
            new("SSL_FINGERPRINT", AgentInstallPropertyKind.Text, "Empreinte du certificat serveur",
                "Empreinte du certificat à accepter, quand il n'est pas vérifiable par une autorité."),
            new("SSL_KEYSTORE", AgentInstallPropertyKind.Text, "Magasins de certificats Windows",
                "« none » pour n'en utiliser aucun, sinon la liste des magasins, séparés par des virgules."),
        ]),

        ("Inventaire",
        [
            new("ITEMTYPE", AgentInstallPropertyKind.Text, "Type d'objet inventorié",
                "Vide pour conserver « Computer »."),
            new("NO_CATEGORY", AgentInstallPropertyKind.Text, "Catégories exclues",
                "Liste séparée par des virgules, parmi celles de « glpi-agent --list-categories » : printer, software, process, environment, user…"),
            new("REQUIRED_CATEGORY", AgentInstallPropertyKind.Text, "Catégories toujours incluses",
                "Mêmes valeurs : ces catégories restent présentes même dans un inventaire partiel."),
            new("FULL_INVENTORY_POSTPONE", AgentInstallPropertyKind.Number, "Inventaires partiels avant un complet",
                "Nombre d'inventaires partiels tolérés avant d'en imposer un complet.", "14"),
            new("SCAN_HOMEDIRS", AgentInstallPropertyKind.Switch, "Analyser les dossiers personnels",
                "Y cherche les machines virtuelles.", "0"),
            new("SCAN_PROFILES", AgentInstallPropertyKind.Switch, "Analyser les profils utilisateurs",
                "Y cherche les logiciels installés par utilisateur.", "0"),
            new("ADDITIONAL_CONTENT", AgentInstallPropertyKind.Text, "Contenu additionnel",
                "Chemin d'un fichier XML (ou du nœud « content » d'un JSON) fusionné dans l'inventaire."),
            new("LOCAL", AgentInstallPropertyKind.Text, "Dossier de sortie local",
                "Chemin où écrire le résultat des tâches. Vide = pas d'écriture locale."),
            new("JSON", AgentInstallPropertyKind.Switch, "Sortie locale en JSON",
                "Désactivé, la sortie locale est en XML. Sans effet sans dossier de sortie local.", "1"),
            new("HTML", AgentInstallPropertyKind.Switch, "Sortie locale en HTML",
                "Remplace le JSON/XML. Sans effet sans dossier de sortie local.", "0"),
            new("ESX_ITEMTYPE", AgentInstallPropertyKind.Text, "Type d'objet pour l'inventaire ESX",
                "Vide pour conserver « Computer »."),
        ]),

        ("Tâches",
        [
            new("TASKS", AgentInstallPropertyKind.Text, "Tâches planifiées, dans l'ordre",
                "Liste séparée par des virgules : Inventory, Deploy, ESX, NetDiscovery, NetInventory, WakeOnLan. Terminer par « ,... » pour ajouter les autres à la suite."),
            new("NO_TASK", AgentInstallPropertyKind.Text, "Tâches désactivées",
                "Mêmes valeurs : Inventory, Deploy, ESX, NetDiscovery, NetInventory, WakeOnLan."),
            new("NO_P2P", AgentInstallPropertyKind.Switch, "Interdire le téléchargement pair-à-pair",
                "Les fichiers de déploiement ne sont plus cherchés auprès des postes voisins.", "0"),
            new("REMOTE", AgentInstallPropertyKind.Text, "Inventaires distants",
                "Définitions au format « remote:définition », pour la tâche RemoteInventory."),
            new("REMOTE_WORKERS", AgentInstallPropertyKind.Number, "Inventaires distants simultanés",
                "Nombre maximum d'inventaires distants menés en parallèle.", "1"),
            new("SNMP_RETRIES", AgentInstallPropertyKind.Number, "Tentatives SNMP",
                "Nombre de reprises quand un équipement ne répond pas, pour NetDiscovery et NetInventory.", "0"),
        ]),

        ("Serveur HTTP local de l'agent",
        [
            new("HTTPD_IP", AgentInstallPropertyKind.Text, "Adresse d'écoute",
                "Adresse IP sur laquelle le serveur intégré écoute.", "0.0.0.0"),
            new("HTTPD_TRUST", AgentInstallPropertyKind.Text, "Adresses de confiance",
                "Adresses dispensées de jeton d'authentification : IP, plages, CIDR ou noms d'hôte, séparés par des virgules.", "127.0.0.1/32"),
            new("LISTEN", AgentInstallPropertyKind.Switch, "Écouter même sans cible",
                "L'interface web reste disponible même sans serveur ni sortie locale configurés.", "0"),
        ]),

        ("Journalisation",
        [
            new("DEBUG", AgentInstallPropertyKind.Choice, "Niveau de débogage", "Verbosité des journaux de l'agent.", "0",
                [("0", "Désactivé"), ("1", "Normal"), ("2", "Complet")]),
            new("LOGGER", AgentInstallPropertyKind.Text, "Destinations des journaux",
                "« file », « stderr », ou les deux séparés par une virgule.", "file"),
            new("LOGFILE", AgentInstallPropertyKind.Text, "Fichier journal",
                "Chemin complet du fichier, utilisé quand « file » fait partie des destinations."),
            new("LOGFILE_MAXSIZE", AgentInstallPropertyKind.Number, "Taille maximale du journal (Mo)",
                "Au delà, le fichier est remis à zéro.", "4"),
        ]),

        ("Installation",
        [
            new("ADDLOCAL", AgentInstallPropertyKind.Text, "Composants installés",
                "Liste séparée par des virgules : ALL, feat_AGENT, feat_NETINV, feat_DEPLOY, feat_COLLECT, feat_ESX, feat_WOL. feat_AGENT est toujours installé."),
            new("FEATURES", AgentInstallPropertyKind.Text, "Fonctions optionnelles",
                "Fonctions désactivées par défaut, séparées par des virgules."),
            new("VARDIR", AgentInstallPropertyKind.Text, "Dossier de données de l'agent",
                "Utile pour placer les données sur un stockage partagé."),
            new("ADD_FIREWALL_EXCEPTION", AgentInstallPropertyKind.Switch, "Ajouter une exception au pare-feu Windows",
                "Nécessaire pour joindre le serveur HTTP local de l'agent depuis le réseau.", "0"),
            new("AGENTMONITOR", AgentInstallPropertyKind.Switch, "Installer GLPI-Agent Monitor",
                "Icône de notification pour l'utilisateur du poste. Mode service uniquement.", "0"),
            new("AGENTMONITOR_NEWTICKET_URL", AgentInstallPropertyKind.Text, "URL de création de ticket",
                "À renseigner quand l'URL devinée par GLPI-Agent Monitor n'est pas la bonne."),
            new("AGENTMONITOR_NEWTICKET_SCREENSHOT", AgentInstallPropertyKind.Switch, "Copie d'écran à la création d'un ticket",
                "GLPI-Agent Monitor joint une copie d'écran à la demande.", "1"),
        ]),
    ];

    /// <summary>Toutes les propriétés, tous groupes confondus.</summary>
    public static IEnumerable<AgentInstallProperty> All => Groups.SelectMany(group => group.Properties);
}
