using System.Text.Json;
using System.Text.Json.Nodes;
using GlpiNg.Modules.Deployment.Models;

namespace GlpiNg.Modules.Deployment.Services;

/// <summary>
/// Construit la réponse JSON de la tâche "wakeonlan" envoyée à GLPI-Agent (voir
/// AgentController.HandleGetWakeOnLanJobsCoreAsync), miroir de <see cref="NetworkJobJsonBuilder"/>.
///
/// <code>
/// {
///   "jobs": [
///     {
///       "uuid": "wol-00000003",
///       "method": "wakeonlan",
///       "targets": [ { "mac": "AA:BB:CC:DD:EE:FF", "computer": 12 } ]
///     }
///   ]
/// }
/// </code>
///
/// Forme auto-inventée (même prudence que <see cref="NetworkJobJsonBuilder"/> : non vérifiée face à
/// un agent réel). Contrairement à <see cref="NetworkJobJsonBuilder"/>, les cibles ne sont pas lues
/// en direct depuis la tâche mais depuis <see cref="WakeOnLanTaskJob.TargetMacsJson"/> — voir la
/// doc de cette classe pour pourquoi ce job fige un instantané au lancement.
/// </summary>
public class WakeOnLanJobJsonBuilder
{
    public JsonObject Build(WakeOnLanTaskJob job, string jobUuid)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(jobUuid);

        List<WakeOnLanTarget> targets = JsonSerializer.Deserialize<List<WakeOnLanTarget>>(job.TargetMacsJson) ?? [];

        JsonArray targetsArray = [];
        foreach (WakeOnLanTarget target in targets)
        {
            targetsArray.Add(new JsonObject
            {
                ["mac"] = target.Mac,
                ["computer"] = target.ComputerId
            });
        }

        JsonObject jobDetails = new()
        {
            ["uuid"] = jobUuid,
            ["method"] = "wakeonlan",
            ["targets"] = targetsArray
        };

        return new JsonObject
        {
            ["jobs"] = new JsonArray { jobDetails }
        };
    }
}
