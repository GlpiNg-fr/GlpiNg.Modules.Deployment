using System.Text.Json;
using System.Text.Json.Nodes;
using GlpiNg.Modules.Deployment.Models;

namespace GlpiNg.Modules.Deployment.Services;

/// <summary>
/// Convertit les listes typées de vérifications/actions/interactions utilisateur éditées
/// depuis l'admin (/tools/deployments) vers/depuis les colonnes JSON brutes de
/// <see cref="DeploymentPackage"/> (ChecksJson/ActionsJson/UserInteractionsJson), qui sont
/// ensuite transmises telles quelles à l'agent par <see cref="DeployJobJsonBuilder"/>.
/// </summary>
public static class DeploymentPackageJsonConverter
{
    private static readonly JsonSerializerOptions SerializeOptions = new() { WriteIndented = false };

    public static JsonArray ParseJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(json) is JsonArray array ? array : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // --- Checks ---

    public static List<DeploymentCheckEntry> ParseChecks(string? json)
    {
        List<DeploymentCheckEntry> entries = [];
        foreach (JsonNode? node in ParseJsonArray(json))
        {
            if (node is not JsonObject obj)
            {
                continue;
            }

            if (!Enum.TryParse((string?)obj["type"], ignoreCase: true, out DeploymentCheckType type))
            {
                continue;
            }

            entries.Add(new DeploymentCheckEntry
            {
                Type = type,
                Path = (string?)obj["path"] ?? string.Empty,
                Value = (string?)obj["value"],
                OnFailure = string.Equals((string?)obj["onFailure"], "continue", StringComparison.OrdinalIgnoreCase)
                    ? DeploymentCheckOnFailure.Continue
                    : DeploymentCheckOnFailure.SkipJob
            });
        }

        return entries;
    }

    public static string SerializeChecks(IEnumerable<DeploymentCheckEntry> entries)
    {
        JsonArray array = [];
        foreach (DeploymentCheckEntry entry in entries)
        {
            array.Add(new JsonObject
            {
                ["type"] = entry.Type.ToString(),
                ["path"] = entry.Path,
                ["value"] = entry.Value,
                ["onFailure"] = entry.OnFailure == DeploymentCheckOnFailure.Continue ? "continue" : "skip-job"
            });
        }

        return array.ToJsonString(SerializeOptions);
    }

    // --- Actions ---

    private static readonly Dictionary<DeploymentActionType, string> ActionWireKeys = new()
    {
        [DeploymentActionType.Command] = "cmd",
        [DeploymentActionType.Move] = "move",
        [DeploymentActionType.Copy] = "copy",
        [DeploymentActionType.DeleteDirectory] = "delete",
        [DeploymentActionType.CreateDirectory] = "mkdir"
    };

    public static List<DeploymentActionEntry> ParseActions(string? json)
    {
        List<DeploymentActionEntry> entries = [];
        foreach (JsonNode? node in ParseJsonArray(json))
        {
            if (node is not JsonObject obj || obj.Count != 1)
            {
                continue;
            }

            (string wireKey, JsonNode? value) = obj.First();
            if (value is not JsonObject body)
            {
                continue;
            }

            DeploymentActionType? type = ActionWireKeys
                .Where(kv => kv.Value == wireKey)
                .Select(kv => (DeploymentActionType?)kv.Key)
                .FirstOrDefault();

            if (type is null)
            {
                continue;
            }

            entries.Add(new DeploymentActionEntry
            {
                Type = type.Value,
                Label = (string?)body["name"] ?? string.Empty,
                Command = (string?)body["exec"],
                LogLineLimit = (int?)body["logLineLimit"] ?? -1,
                ExpectedReturnCode = (int?)body["returnCode"],
                From = (string?)body["from"],
                To = (string?)body["to"],
                Path = (string?)body["path"]
            });
        }

        return entries;
    }

    public static string SerializeActions(IEnumerable<DeploymentActionEntry> entries)
    {
        JsonArray array = [];
        foreach (DeploymentActionEntry entry in entries)
        {
            JsonObject body = entry.Type switch
            {
                DeploymentActionType.Command => new JsonObject
                {
                    ["exec"] = entry.Command,
                    ["name"] = entry.Label,
                    ["logLineLimit"] = entry.LogLineLimit,
                    ["returnCode"] = entry.ExpectedReturnCode
                },
                DeploymentActionType.Move => new JsonObject { ["from"] = entry.From, ["to"] = entry.To, ["name"] = entry.Label },
                DeploymentActionType.Copy => new JsonObject { ["from"] = entry.From, ["to"] = entry.To, ["name"] = entry.Label },
                DeploymentActionType.DeleteDirectory => new JsonObject { ["path"] = entry.Path, ["name"] = entry.Label },
                DeploymentActionType.CreateDirectory => new JsonObject { ["path"] = entry.Path, ["name"] = entry.Label },
                _ => []
            };

            array.Add(new JsonObject { [ActionWireKeys[entry.Type]] = body });
        }

        return array.ToJsonString(SerializeOptions);
    }

    // --- User interactions ---

    public static List<DeploymentUserInteractionEntry> ParseUserInteractions(string? json)
    {
        List<DeploymentUserInteractionEntry> entries = [];
        foreach (JsonNode? node in ParseJsonArray(json))
        {
            if (node is not JsonObject obj)
            {
                continue;
            }

            DeploymentUserInteractionType type = string.Equals((string?)obj["type"], "acceptRefuse", StringComparison.OrdinalIgnoreCase)
                ? DeploymentUserInteractionType.AcceptRefuse
                : DeploymentUserInteractionType.InfoMessage;

            entries.Add(new DeploymentUserInteractionEntry
            {
                Type = type,
                Text = (string?)obj["text"] ?? string.Empty,
                AllowSkip = (bool?)obj["allowSkip"] ?? false
            });
        }

        return entries;
    }

    public static string SerializeUserInteractions(IEnumerable<DeploymentUserInteractionEntry> entries)
    {
        JsonArray array = [];
        foreach (DeploymentUserInteractionEntry entry in entries)
        {
            array.Add(new JsonObject
            {
                ["type"] = entry.Type == DeploymentUserInteractionType.AcceptRefuse ? "acceptRefuse" : "info",
                ["text"] = entry.Text,
                ["allowSkip"] = entry.AllowSkip
            });
        }

        return array.ToJsonString(SerializeOptions);
    }
}
