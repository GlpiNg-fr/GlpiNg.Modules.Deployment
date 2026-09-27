using GlpiNg.Modules.Deployment.Models;
using Microsoft.EntityFrameworkCore;
using GlpiNg.Modules.Abstractions.Localization;

namespace GlpiNg.Modules.Deployment.Services;

public sealed class NetworkTaskLaunchResult
{
    public bool Success { get; init; }
    public required string Message { get; init; }
    public int JobsCreated { get; init; }
}

/// <summary>
/// Lance une <see cref="NetworkTask"/> : crée un <see cref="NetworkTaskJob"/> par acteur
/// (<see cref="NetworkTask.Actors"/>) — plus simple que
/// <see cref="DeploymentTaskLaunchService"/> car il n'y a pas de résolution groupe/ordinateur à
/// faire, un acteur référençant déjà directement l'agent exécutant.
///
/// Contrairement à DeploymentTaskLaunchService, pas de garde "déjà exécutée, relance bloquée sans
/// AllowRePreparation" : un scan réseau (ping/ARP ou lecture SNMP) est par nature idempotent —
/// le relancer ne fait que rafraîchir les <see cref="DiscoveredNetworkDevice"/> déjà connus, sans
/// effet de bord comme réinstaller un paquet — donc toujours permis (simplification assumée par
/// rapport à Deploy).
/// </summary>
public sealed class NetworkTaskLaunchService(IDbContextFactory<DbContext> dbFactory)
{
    public async Task<NetworkTaskLaunchResult> LaunchAsync(int taskId, CancellationToken cancellationToken = default)
    {
        await using DbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        NetworkTask? task = await db.Set<NetworkTask>()
            .Include(t => t.IpRanges)
            .Include(t => t.Credentials)
            .Include(t => t.Actors)
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);

        if (task is null)
        {
            return Fail(Tr.T("Tâche introuvable."));
        }

        if (!task.IsActive)
        {
            return Fail(Tr.T("Cette tâche est désactivée."));
        }

        if (task.IpRanges.Count == 0)
        {
            return Fail(Tr.T("Aucune plage IP configurée pour cette tâche."));
        }

        if (task.Method == NetworkTaskMethod.NetworkInventory && task.Credentials.Count == 0)
        {
            return Fail(Tr.T("Aucun identifiant SNMP configuré pour cette tâche d'inventaire réseau."));
        }

        if (task.Actors.Count == 0)
        {
            return Fail(Tr.T("Aucun acteur (agent) configuré pour cette tâche."));
        }

        task.LastLaunchedAt = DateTime.UtcNow;

        List<NetworkTaskJob> newJobs = task.Actors
            .Select(actor => new NetworkTaskJob
            {
                AgentId = actor.AgentId,
                NetworkTaskId = task.Id,
                Status = NetworkJobStatus.Pending
            })
            .ToList();

        db.Set<NetworkTaskJob>().AddRange(newJobs);
        await db.SaveChangesAsync(cancellationToken);

        return new NetworkTaskLaunchResult
        {
            Success = true,
            Message = Tr.T("{0} job(s) réseau créé(s) ({1} agent(s)).", newJobs.Count, task.Actors.Count),
            JobsCreated = newJobs.Count
        };
    }

    private static NetworkTaskLaunchResult Fail(string message) => new() { Success = false, Message = message };
}
