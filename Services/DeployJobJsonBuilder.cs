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
///       "name": "...", "p2p": "0", "p2p-retention-duration": "0", "uncompress": 0,
///       "mirrors": [ "http://serveur/inventory/deploy/file/part/" ],
///       "multiparts": [ "&lt;part sha512&gt;", ... ]
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
/// d'être corrigée suite à ce constat.
///
/// Chaque entrée de "associatedFiles" doit porter les six clés que _validateAnswer exige
/// (mirrors, multiparts, name, p2p-retention-duration, p2p, uncompress) : il en manquait deux,
/// l'agent répondait « bad JSON: Missing key `mirrors' » (en debug seulement) puis « No Deploy
/// job found », et le job, déjà passé en cours par getJobs, y restait indéfiniment. Un paquet
/// sans fichier n'était pas touché, ce qui masquait le défaut. "multiparts" est une liste de
/// sha512 de fragments, pas d'objets (GLPI::Agent::Task::Deploy::File en fait des chemins), et
/// chaque fragment est téléchargé depuis <c>{mirror}/{c1}/{c1c2}/{sha512}</c>.
/// </summary>
public class DeployJobJsonBuilder
{
    /// <param name="mirrors">URLs de base d'où l'agent télécharge les fragments — il y ajoute
    /// lui-même le chemin <c>{c1}/{c1c2}/{sha512}</c>.</param>
    public JsonObject Build(DeploymentJob job, DeploymentPackage package, string jobUuid, IReadOnlyList<string> mirrors)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(jobUuid);
        ArgumentNullException.ThrowIfNull(mirrors);

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
                multiparts.Add(JsonValue.Create(part.Sha512));
            }

            associatedFileHashes.Add(JsonValue.Create(file.Sha512));
            associatedFilesDetails[file.Sha512] = new JsonObject
            {
                ["name"] = file.FileName,
                ["p2p"] = "0",
                ["p2p-retention-duration"] = "0",
                ["uncompress"] = 0,
                ["mirrors"] = new JsonArray([.. mirrors.Select(mirror => JsonValue.Create(mirror))]),
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
