using System.Text.Json.Nodes;
using GlpiNg.Modules.Deployment.Models;

namespace GlpiNg.Modules.Deployment.Services;

/// <summary>
/// Construit la réponse JSON de la tâche "deploy" envoyée à GLPI-Agent.
///
/// <code>
/// {
///   "jobs": [
///     {
///       "uuid": "...",
///       "checks": [ { "name": "...", "type": "fileExists", "path": "...", "value": "...", "return": "info" } ],
///       "associatedFiles": [ "&lt;sha512&gt;", ... ],
///       "actions": [ { "move": { "from": "...", "to": "...", "name": "..." } }, ... ],
///       "userinteractions": []
///     }
///   ],
///   "associatedFiles": {
///     "&lt;sha512&gt;": {
///       "name": "...", "p2p": "0", "p2p-retention-duration": "0",
///       "multiparts": [ { "sha512": "&lt;part sha512&gt;", "size": 5242880 }, ... ]
///     }
///   }
/// }
/// </code>
///
/// "jobs" est un TABLEAU même quand ce serveur ne renvoie jamais qu'un seul job à la fois (voir
/// AgentController.HandleGetJobsCoreAsync, qui ne sert que le plus ancien DeploymentJob en
/// attente) — confirmé en conditions réelles contre GLPI::Agent::Task::Deploy::_validateAnswer
/// (agent GLPI-Agent 1.18), qui fait <c>foreach my $job (@{$answer->{jobs}})</c> : un objet nu à
/// la place de ce tableau fait mourir le thread Deploy de l'agent (déréférencement d'un hashref
/// comme tableau sous `use strict`) sans qu'aucune erreur ne remonte au log agent ni au serveur —
/// silencieux des deux côtés, à ne pas confondre avec un job qui n'aurait simplement rien à faire.
/// Cette erreur de forme a existé un temps dans ce fichier (objet nu plutôt que tableau) avant
/// d'être corrigée suite à ce constat ; le champ "uuid" (identifiant interne du
/// <see cref="DeploymentJob"/>) et "multiparts" (calqué sur <see cref="DeploymentPackageFilePart"/>)
/// restent des adaptations non vérifiées au-delà de ce qui précède.
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

        JsonObject jobDetails = new()
        {
            ["uuid"] = jobUuid,
            ["checks"] = checks,
            ["associatedFiles"] = associatedFileHashes,
            ["actions"] = actions,
            ["userinteractions"] = userInteractions
        };

        return new JsonObject
        {
            ["jobs"] = new JsonArray { jobDetails },
            ["associatedFiles"] = associatedFilesDetails
        };
    }
}
