using System.Text.Json.Nodes;
using GlpiNg.Modules.Deployment.Models;

namespace GlpiNg.Modules.Deployment.Services;

/// <summary>
/// Construit la réponse JSON de la tâche "deploy" envoyée à GLPI-Agent.
///
/// Format vérifié par recoupement d'échanges réels agent/serveur (retours de la
/// communauté GLPI-Project) :
/// <code>
/// {
///   "jobs": {
///     "uuid": "...",
///     "checks": [ { "name": "...", "type": "fileExists", "path": "...", "value": "...", "return": "info" } ],
///     "associatedFiles": [ "&lt;sha512&gt;", ... ],
///     "actions": [ { "move": { "from": "...", "to": "...", "name": "..." } }, ... ],
///     "userinteractions": []
///   },
///   "associatedFiles": {
///     "&lt;sha512&gt;": {
///       "name": "...", "p2p": "0", "p2p-retention-duration": "0",
///       "multiparts": [ { "sha512": "&lt;part sha512&gt;", "size": 5242880 }, ... ]
///     }
///   }
/// }
/// </code>
///
/// NB : le champ "uuid" utilisé ici sert de corrélation interne (identifiant du
/// <see cref="DeploymentJob"/>) — le protocole réel du plugin GlpiInventory utilise un
/// endpoint séparé à base de query-string (action=getJobs/setStatus) que ce serveur
/// choisit de ne pas reproduire tel quel : ce point est une adaptation, pas une donnée
/// vérifiée du protocole d'origine. Le champ "multiparts" reprend l'esprit du découpage en
/// fragments du protocole réel (téléchargement/vérification fragment par fragment par
/// l'agent, voir <see cref="DeploymentPackageFilePart"/>) sans prétendre à une fidélité
/// exacte de forme, faute de spécification de référence accessible pour ce point précis.
/// </summary>
public class DeployJobJsonBuilder
{
    public JsonObject Build(DeploymentJob job, DeploymentPackage package, string jobUuid)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(jobUuid);

        JsonArray checks = DeploymentPackageJsonConverter.ParseJsonArray(package.ChecksJson);
        JsonArray actions = DeploymentPackageJsonConverter.ParseJsonArray(package.ActionsJson);
        JsonArray userInteractions = DeploymentPackageJsonConverter.ParseJsonArray(package.UserInteractionsJson);

        JsonArray associatedFileHashes = [];
        JsonObject associatedFilesDetails = [];

        foreach (DeploymentPackageFile file in package.Files)
        {
            JsonArray multiparts = [];
            foreach (DeploymentPackageFilePart part in file.Parts.OrderBy(p => p.PartIndex))
            {
                multiparts.Add(new JsonObject
                {
                    ["sha512"] = part.Sha512,
                    ["size"] = part.SizeBytes
                });
            }

            associatedFileHashes.Add(JsonValue.Create(file.Sha512));
            associatedFilesDetails[file.Sha512] = new JsonObject
            {
                ["name"] = file.FileName,
                ["p2p"] = "0",
                ["p2p-retention-duration"] = "0",
                ["multiparts"] = multiparts
            };
        }

        JsonObject jobs = new()
        {
            ["uuid"] = jobUuid,
            ["checks"] = checks,
            ["associatedFiles"] = associatedFileHashes,
            ["actions"] = actions,
            ["userinteractions"] = userInteractions
        };

        return new JsonObject
        {
            ["jobs"] = jobs,
            ["associatedFiles"] = associatedFilesDetails
        };
    }
}
