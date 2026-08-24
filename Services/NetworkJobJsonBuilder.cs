using System.Text.Json.Nodes;
using GlpiNg.Modules.Deployment.Models;

namespace GlpiNg.Modules.Deployment.Services;

/// <summary>
/// Construit la réponse JSON des tâches "netdiscovery"/"netinventory" envoyée à GLPI-Agent (voir
/// AgentController.HandleGetNetworkJobsCoreAsync), miroir de <see cref="DeployJobJsonBuilder"/>
/// pour la tâche "deploy".
///
/// <code>
/// {
///   "jobs": [
///     {
///       "uuid": "net-00000012",
///       "method": "netdiscovery",
///       "ranges": [ { "name": "...", "start": "10.0.0.1", "end": "10.0.0.254" } ],
///       "credentials": [ { "id": 3, "version": "2c", "community": "public" }, ... ]
///     }
///   ]
/// }
/// </code>
///
/// Forme auto-inventée (voir le commentaire de classe d'AgentController sur les tâches
/// netdiscovery/netinventory) : "credentials" est toujours vide pour une découverte réseau
/// (ping/ARP, pas d'interrogation SNMP) et rempli pour un inventaire réseau. Comme
/// <see cref="DeployJobJsonBuilder"/>, "jobs" reste un TABLEAU même à un seul élément — l'agent réel
/// pour "deploy" meurt silencieusement sur un objet nu à la place, on applique la même prudence ici
/// par cohérence bien qu'aucune vérification en conditions réelles n'existe pour ces deux tâches.
/// </summary>
public class NetworkJobJsonBuilder
{
    public JsonObject Build(NetworkTaskJob job, NetworkTask task, string jobUuid)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(jobUuid);

        JsonArray ranges = [];
        foreach (NetworkTaskIpRange range in task.IpRanges)
        {
            if (range.IpRange is null) continue;

            ranges.Add(new JsonObject
            {
                ["name"] = range.IpRange.Name,
                ["start"] = range.IpRange.StartIp,
                ["end"] = range.IpRange.EndIp
            });
        }

        JsonArray credentials = [];
        if (task.Method == NetworkTaskMethod.NetworkInventory)
        {
            foreach (NetworkTaskCredential credential in task.Credentials)
            {
                if (credential.SnmpCredential is null) continue;

                credentials.Add(BuildCredential(credential.SnmpCredential));
            }
        }

        JsonObject jobDetails = new()
        {
            ["uuid"] = jobUuid,
            ["method"] = task.Method == NetworkTaskMethod.NetworkDiscovery ? "netdiscovery" : "netinventory",
            ["ranges"] = ranges,
            ["credentials"] = credentials
        };

        return new JsonObject
        {
            ["jobs"] = new JsonArray { jobDetails }
        };
    }

    private static JsonObject BuildCredential(SnmpCredential credential)
    {
        JsonObject json = new()
        {
            ["id"] = credential.Id,
            ["version"] = credential.Version switch
            {
                SnmpVersion.V1 => "1",
                SnmpVersion.V2c => "2c",
                SnmpVersion.V3 => "3",
                _ => "2c"
            }
        };

        if (credential.Version is SnmpVersion.V1 or SnmpVersion.V2c)
        {
            json["community"] = credential.Community;
        }
        else
        {
            json["username"] = credential.Username;
            json["authprotocol"] = credential.AuthProtocol.ToString();
            json["authpassphrase"] = credential.AuthPassphrase;
            json["privprotocol"] = credential.PrivProtocol.ToString();
            json["privpassphrase"] = credential.PrivPassphrase;
        }

        return json;
    }
}
