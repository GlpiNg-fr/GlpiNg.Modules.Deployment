namespace GlpiNg.Modules.Deployment.Models;

/// <summary>
/// Types de vérification (audit) disponibles avant/pendant un job de déploiement.
/// Reprend la liste proposée par l'onglet "Actions sur le paquet" du plugin GLPI-Inventory
/// (front/deploypackage.form.php), sans prétendre reproduire exactement les identifiants
/// internes du protocole GLPI-Agent — voir la remarque d'adaptation dans <c>DeployJobJsonBuilder</c>.
/// </summary>
public enum DeploymentCheckType
{
    FileExists,
    FileNotExists,
    FileSizeGreater,
    FileSizeEquals,
    FileSizeLower,
    FileSha512Equals,
    FileSha512NotEquals,
    DirectoryExists,
    DirectoryNotExists,
    FreeSpaceGreater,
    RegistryKeyExists,
    RegistryKeyNotExists,
    RegistryValueExists,
    RegistryValueNotExists,
    RegistryValueEquals,
    RegistryValueNotEquals
}

/// <summary>Comportement du job si la vérification échoue.</summary>
public enum DeploymentCheckOnFailure
{
    SkipJob,
    Continue
}

public class DeploymentCheckEntry
{
    public DeploymentCheckType Type { get; set; } = DeploymentCheckType.FileExists;

    /// <summary>Chemin de fichier/répertoire ou clef de registre visé par la vérification.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Valeur attendue (taille, hash SHA-512, valeur de registre...), selon le type.</summary>
    public string? Value { get; set; }

    public DeploymentCheckOnFailure OnFailure { get; set; } = DeploymentCheckOnFailure.SkipJob;
}

/// <summary>Types d'action exécutables par l'agent (cmd/move/copy/delete/mkdir).</summary>
public enum DeploymentActionType
{
    Command,
    Move,
    Copy,
    DeleteDirectory,
    CreateDirectory
}

public class DeploymentActionEntry
{
    public DeploymentActionType Type { get; set; } = DeploymentActionType.Command;

    /// <summary>Libellé affiché pour cette action (ex: "Installation").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Commande à exécuter (type Command uniquement).</summary>
    public string? Command { get; set; }

    /// <summary>Nombre de lignes de sortie à récupérer (-1 = illimité), type Command uniquement.</summary>
    public int LogLineLimit { get; set; } = -1;

    /// <summary>Code de retour attendu pour considérer l'action réussie (type Command uniquement).</summary>
    public int? ExpectedReturnCode { get; set; }

    /// <summary>Chemin source (types Move/Copy).</summary>
    public string? From { get; set; }

    /// <summary>Chemin destination (types Move/Copy).</summary>
    public string? To { get; set; }

    /// <summary>Chemin du répertoire ciblé (types DeleteDirectory/CreateDirectory).</summary>
    public string? Path { get; set; }
}

/// <summary>Types d'interaction utilisateur affichée par l'agent pendant le job.</summary>
public enum DeploymentUserInteractionType
{
    InfoMessage,
    AcceptRefuse
}

public class DeploymentUserInteractionEntry
{
    public DeploymentUserInteractionType Type { get; set; } = DeploymentUserInteractionType.InfoMessage;

    public string Text { get; set; } = string.Empty;

    /// <summary>Si vrai, l'utilisateur peut ignorer cette interaction sans bloquer le job.</summary>
    public bool AllowSkip { get; set; }
}
