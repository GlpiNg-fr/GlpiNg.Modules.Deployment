using System.Text.Json;
using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Deployment.Services;

public sealed class WakeOnLanTaskLaunchResult
{
    public bool Success { get; init; }
    public required string Message { get; init; }
    public int JobsCreated { get; init; }
}

/// <summary>Une cible résolue, sérialisée dans <see cref="WakeOnLanTaskJob.TargetMacsJson"/>.</summary>
public sealed record WakeOnLanTarget(int ComputerId, string Mac);

/// <summary>
/// Lance une <see cref="WakeOnLanTask"/> : résout ses <see cref="WakeOnLanTask.Targets"/> en
/// ordinateurs (même logique que <see cref="DeploymentTaskLaunchService"/> — membres des groupes
/// statiques, correspondances des groupes dynamiques via <see cref="DeployGroupCriteriaEvaluator"/>,
/// ordinateurs individuels), puis en adresses MAC via <see cref="Computer.NetworkPorts"/> (seule
/// source de MAC du modèle — ni <see cref="GlpiAgent"/> ni <see cref="WakeOnLanTaskTarget"/> n'en
/// portent), et fige le résultat dans <see cref="WakeOnLanTaskJob.TargetMacsJson"/> à la création
/// d'un job par agent relais (<see cref="WakeOnLanTask.RelayAgents"/>) — voir la doc de
/// <see cref="WakeOnLanTaskJob"/> pour pourquoi ce snapshot n'est pas lu en direct comme
/// <see cref="NetworkTaskJob"/>.
/// </summary>
public sealed class WakeOnLanTaskLaunchService(IDbContextFactory<DbContext> dbFactory)
{
    public async Task<WakeOnLanTaskLaunchResult> LaunchAsync(int taskId, CancellationToken cancellationToken = default)
    {
        await using DbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        WakeOnLanTask? task = await db.Set<WakeOnLanTask>()
            .Include(t => t.Targets).ThenInclude(target => target.Group).ThenInclude(g => g!.Members)
            .Include(t => t.Targets).ThenInclude(target => target.Group).ThenInclude(g => g!.Criteria)
            .Include(t => t.RelayAgents)
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);

        if (task is null)
        {
            return Fail("Tâche introuvable.");
        }

        if (!task.IsActive)
        {
            return Fail("Cette tâche est désactivée.");
        }

        if (task.Targets.Count == 0)
        {
            return Fail("Aucune cible configurée pour cette tâche.");
        }

        if (task.RelayAgents.Count == 0)
        {
            return Fail("Aucun agent relais configuré pour cette tâche.");
        }

        HashSet<int> computerIds = [];
        List<Computer>? allComputers = null;
        foreach (WakeOnLanTaskTarget target in task.Targets)
        {
            if (target.Type == WakeOnLanTaskTargetType.Computer)
            {
                if (target.ComputerId is { } computerId)
                {
                    computerIds.Add(computerId);
                }
            }
            else if (target.Group is not null)
            {
                if (target.Group.Type == DeployComputerGroupType.Static)
                {
                    foreach (DeployComputerGroupMember member in target.Group.Members)
                    {
                        computerIds.Add(member.ComputerId);
                    }
                }
                else
                {
                    allComputers ??= await db.Set<Computer>().AsNoTracking().Include(c => c.NetworkPorts).ToListAsync(cancellationToken);
                    foreach (Computer computer in DeployGroupCriteriaEvaluator.Filter(allComputers, target.Group.Criteria))
                    {
                        computerIds.Add(computer.Id);
                    }
                }
            }
        }

        List<Computer> targetComputers = allComputers?.Where(c => computerIds.Contains(c.Id)).ToList()
            ?? await db.Set<Computer>().AsNoTracking().Include(c => c.NetworkPorts).Where(c => computerIds.Contains(c.Id)).ToListAsync(cancellationToken);

        List<WakeOnLanTarget> resolvedTargets = targetComputers
            .Select(c => new { c.Id, Mac = ResolveMacAddress(c) })
            .Where(x => x.Mac is not null)
            .Select(x => new WakeOnLanTarget(x.Id, x.Mac!))
            .ToList();

        task.LastLaunchedAt = DateTime.UtcNow;

        if (resolvedTargets.Count == 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            return Fail("Aucun ordinateur ciblé n'a d'adresse MAC connue (voir l'inventaire réseau).");
        }

        string targetMacsJson = JsonSerializer.Serialize(resolvedTargets);

        List<WakeOnLanTaskJob> newJobs = task.RelayAgents
            .Select(actor => new WakeOnLanTaskJob
            {
                AgentId = actor.AgentId,
                WakeOnLanTaskId = task.Id,
                Status = WakeOnLanJobStatus.Pending,
                TargetMacsJson = targetMacsJson
            })
            .ToList();

        db.Set<WakeOnLanTaskJob>().AddRange(newJobs);
        await db.SaveChangesAsync(cancellationToken);

        return new WakeOnLanTaskLaunchResult
        {
            Success = true,
            Message = $"{newJobs.Count} job(s) de réveil créé(s) ({resolvedTargets.Count} ordinateur(s) × {task.RelayAgents.Count} agent(s) relais).",
            JobsCreated = newJobs.Count
        };
    }

    /// <summary>Première MAC utilisable d'un ordinateur, en préférant une carte non virtuelle
    /// (les interfaces virtuelles — VPN, adaptateurs Hyper-V/VMware... — ne mènent nulle part sur
    /// le réseau physique où le magic packet doit être diffusé).</summary>
    private static string? ResolveMacAddress(Computer computer) =>
        computer.NetworkPorts
            .Where(p => !string.IsNullOrWhiteSpace(p.MacAddress))
            .OrderBy(p => p.IsVirtual)
            .Select(p => p.MacAddress)
            .FirstOrDefault();

    private static WakeOnLanTaskLaunchResult Fail(string message) => new() { Success = false, Message = message };
}
