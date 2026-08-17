using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Deployment.Services;

public sealed class DeploymentTaskLaunchResult
{
    public bool Success { get; init; }
    public required string Message { get; init; }
    public int JobsCreated { get; init; }
}

/// <summary>
/// Lance une <see cref="DeploymentTask"/> : résout l'ensemble des ordinateurs couverts par ses
/// acteurs (<see cref="DeploymentTask.Targets"/> — membres des groupes statiques, correspondances
/// des groupes dynamiques via <see cref="DeployGroupCriteriaEvaluator"/>, et ordinateurs
/// individuels), puis crée un <see cref="DeploymentJob"/> pour chaque combinaison (agent GLPI-Agent
/// × paquet de <see cref="DeploymentTask.Packages"/>) — même mécanique que
/// <see cref="ComputerDeploymentAssignmentService.AssignPackagesAsync"/>, mais pour tous les
/// acteurs et paquets de la tâche en une fois plutôt qu'un seul poste/paquet.
/// </summary>
public sealed class DeploymentTaskLaunchService(IDbContextFactory<DbContext> dbFactory)
{
    public async Task<DeploymentTaskLaunchResult> LaunchAsync(int taskId, CancellationToken cancellationToken = default)
    {
        await using DbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        DeploymentTask? task = await db.Set<DeploymentTask>()
            .Include(t => t.Packages)
            .Include(t => t.Targets).ThenInclude(target => target.Group).ThenInclude(g => g!.Members)
            .Include(t => t.Targets).ThenInclude(target => target.Group).ThenInclude(g => g!.Criteria)
            .Include(t => t.Jobs)
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);

        if (task is null)
        {
            return Fail("Tâche introuvable.");
        }

        if (!task.IsActive)
        {
            return Fail("Cette tâche est désactivée.");
        }

        List<int> packageIds = task.Packages.Select(p => p.PackageId).Distinct().ToList();
        if (packageIds.Count == 0)
        {
            return Fail("Aucun paquet configuré pour cette tâche.");
        }

        if (task.Targets.Count == 0)
        {
            return Fail("Aucun acteur configuré pour cette tâche.");
        }

        int existingPackageCount = await db.Set<DeploymentPackage>().CountAsync(p => packageIds.Contains(p.Id), cancellationToken);
        if (existingPackageCount != packageIds.Count)
        {
            return Fail("Au moins un des paquets associés à cette tâche n'existe plus.");
        }

        // Une fois tous les jobs d'une tâche dans un état terminal, on refuse de la relancer sans
        // que l'admin ait explicitement coché AllowRePreparation — évite un redéploiement
        // accidentel des mêmes paquets sur des cibles déjà traitées (voir sa doc).
        bool hasCompletedRun = task.Jobs.Count > 0 && task.Jobs.All(j => j.Status is DeploymentStatus.Success or DeploymentStatus.Error);
        if (hasCompletedRun && !task.AllowRePreparation)
        {
            return Fail("Cette tâche a déjà été exécutée. Activez « Permet la re-préparation de la tâche après son exécution » pour la relancer.");
        }

        HashSet<int> computerIds = [];
        List<Computer>? allComputers = null;
        foreach (DeploymentTaskTarget target in task.Targets)
        {
            if (target.Type == DeploymentTaskTargetType.Computer)
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
                    allComputers ??= await db.Set<Computer>().AsNoTracking().ToListAsync(cancellationToken);
                    foreach (Computer computer in DeployGroupCriteriaEvaluator.Filter(allComputers, target.Group.Criteria))
                    {
                        computerIds.Add(computer.Id);
                    }
                }
            }
        }

        List<int> agentIds = await db.Set<Computer>()
            .AsNoTracking()
            .Where(c => computerIds.Contains(c.Id) && c.AgentId != null)
            .Select(c => c.AgentId!.Value)
            .ToListAsync(cancellationToken);

        task.LastLaunchedAt = DateTime.UtcNow;

        if (agentIds.Count == 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            return Fail("Aucun ordinateur ciblé par cette tâche n'a d'agent GLPI-Agent associé.");
        }

        List<DeploymentJob> newJobs = [];
        foreach (int agentId in agentIds)
        {
            foreach (int packageId in packageIds)
            {
                newJobs.Add(new DeploymentJob
                {
                    AgentId = agentId,
                    PackageId = packageId,
                    TaskId = task.Id,
                    Status = DeploymentStatus.Pending
                });
            }
        }

        db.Set<DeploymentJob>().AddRange(newJobs);
        await db.SaveChangesAsync(cancellationToken);

        return new DeploymentTaskLaunchResult
        {
            Success = true,
            Message = $"{newJobs.Count} job(s) de déploiement créé(s) ({agentIds.Count} agent(s) × {packageIds.Count} paquet(s)).",
            JobsCreated = newJobs.Count
        };
    }

    private static DeploymentTaskLaunchResult Fail(string message) => new() { Success = false, Message = message };
}
