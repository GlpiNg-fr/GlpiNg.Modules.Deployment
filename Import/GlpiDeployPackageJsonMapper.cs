using System.Text.Json;
using System.Text.Json.Nodes;
using GlpiNg.Modules.Deployment.Models;

namespace GlpiNg.Modules.Deployment.Import;

/// <summary>Un fichier attendu par un paquet importé, dont seul le descripteur est connu.</summary>
/// <param name="Sha512">Empreinte servant de clé dans le protocole agent.</param>
/// <param name="FileName">Nom d'origine, tel que déclaré dans le paquet GLPI.</param>
public sealed record GlpiDeployFileReference(string Sha512, string? FileName);

/// <summary>Ce qu'un document de paquet GLPI a livré, et ce qu'il n'a pas été possible de reprendre.</summary>
public sealed class GlpiDeployPackageContent
{
    public List<DeploymentCheckEntry> Checks { get; } = [];
    public List<DeploymentActionEntry> Actions { get; } = [];
    public List<DeploymentUserInteractionEntry> UserInteractions { get; } = [];

    /// <summary>Fichiers référencés par le paquet. Leur contenu vit sur le disque du serveur GLPI,
    /// hors de portée d'un import qui ne lit que la base — seul le descripteur est repris.</summary>
    public List<GlpiDeployFileReference> Files { get; } = [];

    /// <summary>Éléments reconnus comme tels mais sans équivalent dans GlpiNg, à signaler à l'admin
    /// plutôt qu'à laisser disparaître en silence.</summary>
    public List<string> Unsupported { get; } = [];

    public bool IsEmpty => Checks.Count == 0 && Actions.Count == 0 && UserInteractions.Count == 0 && Files.Count == 0;
}

/// <summary>
/// Traduit le document JSON qu'un paquet de GLPI Inventory / FusionInventory porte dans la colonne
/// <c>json</c> de sa table, vers les vérifications, actions et interactions de GlpiNg.
///
/// Contrairement aux fichiers du paquet — qui vivent sur le disque du serveur GLPI et qu'un import
/// lisant la base ne peut pas rapatrier — ce document est bien en base : c'est lui qui porte le
/// travail réel d'un paquet, les commandes et les conditions, celui qu'on ne veut pas ressaisir.
///
/// La lecture est volontairement permissive. Le format a bougé entre FusionInventory et GLPI
/// Inventory et d'une version à l'autre : une clé inattendue, un type inconnu ou un nombre écrit
/// comme une chaîne ne doivent pas faire échouer l'import du paquet entier. Ce qui n'est pas
/// reconnu part dans <see cref="GlpiDeployPackageContent.Unsupported"/> et remonte à
/// l'administrateur, qui saura quoi recréer à la main.
/// </summary>
public static class GlpiDeployPackageJsonMapper
{
    public static GlpiDeployPackageContent Parse(string? json)
    {
        GlpiDeployPackageContent content = new();

        if (string.IsNullOrWhiteSpace(json))
        {
            return content;
        }

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            content.Unsupported.Add("document JSON illisible");
            return content;
        }

        if (root is null)
        {
            return content;
        }

        JsonObject? jobs = root["jobs"] as JsonObject;

        ReadChecks(jobs?["checks"] as JsonArray, content);
        ReadActions(jobs?["actions"] as JsonArray, content);
        ReadUserInteractions(jobs?["userinteractions"] as JsonArray, content);
        ReadFiles(root["associatedFiles"], jobs?["associatedFiles"] as JsonArray, content);

        return content;
    }

    private static void ReadChecks(JsonArray? checks, GlpiDeployPackageContent content)
    {
        foreach (JsonObject check in Objects(checks))
        {
            string? type = Text(check["type"]);

            if (MapCheckType(type) is not { } mapped)
            {
                content.Unsupported.Add($"vérification « {type ?? "sans type"} »");
                continue;
            }

            content.Checks.Add(new DeploymentCheckEntry
            {
                Type = mapped,
                Path = Text(check["path"]) ?? string.Empty,
                Value = Text(check["value"]),
                // « error » interrompt le job côté GLPI ; « ignore », « info » et « warning » le
                // laissent continuer. GlpiNg n'a que ces deux comportements.
                OnFailure = Text(check["return"]) == "error"
                    ? DeploymentCheckOnFailure.SkipJob
                    : DeploymentCheckOnFailure.Continue,
            });
        }
    }

    private static DeploymentCheckType? MapCheckType(string? type) => type switch
    {
        "fileExists" => DeploymentCheckType.FileExists,
        "fileMissing" => DeploymentCheckType.FileNotExists,
        "fileSizeEquals" => DeploymentCheckType.FileSizeEquals,
        "fileSizeGreater" => DeploymentCheckType.FileSizeGreater,
        "fileSizeLower" => DeploymentCheckType.FileSizeLower,
        "fileSHA512" => DeploymentCheckType.FileSha512Equals,
        "fileSHA512mismatch" => DeploymentCheckType.FileSha512NotEquals,
        "directoryExists" => DeploymentCheckType.DirectoryExists,
        "directoryMissing" => DeploymentCheckType.DirectoryNotExists,
        "freespaceGreater" => DeploymentCheckType.FreeSpaceGreater,
        "winkeyExists" => DeploymentCheckType.RegistryKeyExists,
        "winkeyMissing" => DeploymentCheckType.RegistryKeyNotExists,
        "winvalueExists" => DeploymentCheckType.RegistryValueExists,
        "winvalueMissing" => DeploymentCheckType.RegistryValueNotExists,
        "winvalueEquals" => DeploymentCheckType.RegistryValueEquals,
        "winvalueNotEquals" => DeploymentCheckType.RegistryValueNotEquals,
        _ => null,
    };

    /// <summary>
    /// Chaque action est un objet à une seule clé, qui donne son type : <c>cmd</c>, <c>move</c>,
    /// <c>copy</c>, <c>delete</c>, <c>mkdir</c>.
    /// </summary>
    private static void ReadActions(JsonArray? actions, GlpiDeployPackageContent content)
    {
        foreach (JsonObject action in Objects(actions))
        {
            foreach ((string key, JsonNode? node) in action)
            {
                JsonObject body = node as JsonObject ?? [];

                switch (key)
                {
                    case "cmd":
                        content.Actions.Add(new DeploymentActionEntry
                        {
                            Type = DeploymentActionType.Command,
                            Label = Text(body["name"]) ?? "Commande",
                            Command = Text(body["exec"]),
                            LogLineLimit = Number(body["logLineLimit"]) ?? -1,
                            ExpectedReturnCode = ExpectedReturnCode(body),
                        });
                        break;

                    case "move":
                    case "copy":
                        content.Actions.Add(new DeploymentActionEntry
                        {
                            Type = key == "move" ? DeploymentActionType.Move : DeploymentActionType.Copy,
                            Label = Text(body["name"]) ?? (key == "move" ? "Déplacement" : "Copie"),
                            From = Text(body["from"]),
                            To = Text(body["to"]),
                        });
                        break;

                    case "delete":
                    case "mkdir":
                        // Ces deux actions portent une liste de chemins ; GlpiNg n'en cible qu'un
                        // par action, donc une action par chemin.
                        foreach (string path in Paths(body))
                        {
                            content.Actions.Add(new DeploymentActionEntry
                            {
                                Type = key == "delete" ? DeploymentActionType.DeleteDirectory : DeploymentActionType.CreateDirectory,
                                Label = key == "delete" ? "Suppression" : "Création de répertoire",
                                Path = path,
                            });
                        }
                        break;

                    default:
                        content.Unsupported.Add($"action « {key} »");
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Code de retour attendu, tiré de la première contrainte <c>okCode</c> du bloc
    /// <c>retChecks</c>. GlpiNg n'en retient qu'un, là où GLPI en accepte une liste : les autres
    /// sont perdus, ce qui vaut mieux que de perdre le premier aussi.
    /// </summary>
    private static int? ExpectedReturnCode(JsonObject command)
    {
        foreach (JsonObject retCheck in Objects(command["retChecks"] as JsonArray))
        {
            if (Text(retCheck["type"]) != "okCode")
            {
                continue;
            }

            foreach (JsonNode? value in retCheck["values"] as JsonArray ?? [])
            {
                if (Number(value) is { } code)
                {
                    return code;
                }
            }
        }

        return null;
    }

    private static void ReadUserInteractions(JsonArray? interactions, GlpiDeployPackageContent content)
    {
        foreach (JsonObject interaction in Objects(interactions))
        {
            string? text = Text(interaction["text"]) ?? Text(interaction["message"]);

            if (string.IsNullOrWhiteSpace(text))
            {
                // Une interaction de GLPI Inventory peut se réduire à une référence de gabarit,
                // dont le texte vit dans une autre table : rien d'exploitable ici.
                content.Unsupported.Add("interaction utilisateur sans texte (gabarit référencé)");
                continue;
            }

            content.UserInteractions.Add(new DeploymentUserInteractionEntry
            {
                Type = Text(interaction["behavior"]) is "ok" or "okcancel" or "yesno"
                    ? DeploymentUserInteractionType.AcceptRefuse
                    : DeploymentUserInteractionType.InfoMessage,
                Text = text,
            });
        }
    }

    /// <summary>
    /// Les fichiers sont référencés deux fois dans le document : par leurs empreintes dans
    /// <c>jobs.associatedFiles</c>, et par un descripteur (nom, type MIME...) dans
    /// <c>associatedFiles</c> à la racine. On lit le second, et on complète avec le premier pour ne
    /// perdre aucune empreinte si les deux ne concordent pas.
    /// </summary>
    private static void ReadFiles(JsonNode? descriptors, JsonArray? hashes, GlpiDeployPackageContent content)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        if (descriptors is JsonObject byHash)
        {
            foreach ((string sha512, JsonNode? descriptor) in byHash)
            {
                if (string.IsNullOrWhiteSpace(sha512) || !seen.Add(sha512))
                {
                    continue;
                }

                content.Files.Add(new GlpiDeployFileReference(sha512, Text((descriptor as JsonObject)?["name"])));
            }
        }

        foreach (JsonNode? hash in hashes ?? [])
        {
            if (Text(hash) is { Length: > 0 } sha512 && seen.Add(sha512))
            {
                content.Files.Add(new GlpiDeployFileReference(sha512, null));
            }
        }
    }

    private static IEnumerable<JsonObject> Objects(JsonArray? array) =>
        (array ?? []).OfType<JsonObject>();

    private static IEnumerable<string> Paths(JsonObject body)
    {
        if (body["list"] is JsonArray list)
        {
            foreach (JsonNode? entry in list)
            {
                if (Text(entry) is { Length: > 0 } path)
                {
                    yield return path;
                }
            }
        }
        else if (Text(body["path"]) is { Length: > 0 } single)
        {
            yield return single;
        }
    }

    /// <summary>Lit une valeur scalaire comme texte : le plugin écrit indifféremment des chaînes,
    /// des nombres et des booléens pour un même champ selon sa version.</summary>
    private static string? Text(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        string? text = value.TryGetValue(out string? asString) ? asString : value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static int? Number(JsonNode? node) =>
        int.TryParse(Text(node), out int parsed) ? parsed : null;
}
