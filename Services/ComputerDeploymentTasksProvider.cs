using GlpiNg.Modules.Abstractions.Deployment;
using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Deployment.Services;

/// <summary>
/// Implémentation Web de <see cref="IComputerDeploymentTasksProvider"/> : alimente l'onglet
/// "Tâches / groupes" de la fiche Ordinateur (module Inventory) à partir des
/// <see cref="DeploymentTask"/> qui ciblent ce poste — directement (acteur Ordinateur) ou via
/// l'un de ses groupes (acteur Groupe, statique ou dynamique, voir
/// <see cref="DeployGroupCriteriaEvaluator"/>) — et des <see cref="DeployComputerGroup"/> dont il
/// est membre. Une tâche apparaît dans la liste dès qu'elle cible le poste, même si elle n'a
/// jamais encore été lancée ; une tâche qui ne le cible plus mais l'a déjà ciblé par le passé
/// (l'agent a des <see cref="DeploymentJob"/> à son actif pour cette tâche) reste visible pour
/// conserver l'historique — voir <see cref="TargetsComputer"/>.
/// </summary>
public sealed class ComputerDeploymentTasksProvider(IDbContextFactory<DbContext> dbFactory) : IComputerDeploymentTasksProvider
{
    public async Task<ComputerDeploymentTasksInfo> GetForComputerAsync(int computerId, CancellationToken cancellationToken = default)
    {
        await using DbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        Computer? computer = await db.Set<Computer>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken);

        var info = new ComputerDeploymentTasksInfo();
        if (computer is null)
        {
            return info;
        }

        List<DeploymentTask> tasks = await db.Set<DeploymentTask>()
            .AsNoTracking()
            .Include(t => t.Targets).ThenInclude(target => target.Group).ThenInclude(g => g!.Members)
            .Include(t => t.Targets).ThenInclude(target => target.Group).ThenInclude(g => g!.Criteria)
            .ToListAsync(cancellationToken);

        Dictionary<int, List<DeploymentJob>> jobsByTaskId = [];
        if (computer.AgentId is { } agentId)
        {
            List<DeploymentJob> agentJobs = await db.Set<DeploymentJob>()
                .AsNoTracking()
                .Where(j => j.AgentId == agentId && j.TaskId != null)
                .ToListAsync(cancellationToken);

            jobsByTaskId = agentJobs
                .GroupBy(j => j.TaskId!.Value)
                .ToDictionary(group => group.Key, group => group.ToList());
        }

        info.Tasks = tasks
            .Where(task => TargetsComputer(task, computer) || jobsByTaskId.ContainsKey(task.Id))
            .Select(task => new ComputerDeploymentTask
            {
                TaskId = task.Id,
                Name = task.Name,
                Active = task.IsActive,
                MethodLabel = "Déploiement de package",
                Executions = (jobsByTaskId.TryGetValue(task.Id, out List<DeploymentJob>? jobs) ? jobs : [])
                    .OrderByDescending(j => j.StartedAt ?? j.CreatedAt)
                    .Select(j => new ComputerDeploymentTaskExecution
                    {
                        DateUtc = j.StartedAt ?? j.CreatedAt,
                        StatusLabel = StatusLabel(j.Status),
                        StatusBadgeCssClass = StatusBadgeCssClass(j.Status)
                    })
                    .ToList()
            })
            .OrderByDescending(t => t.Executions.Count > 0 ? t.Executions[0].DateUtc : null)
            .ThenBy(t => t.Name)
            .ToList();

        List<DeployComputerGroup> groups = await db.Set<DeployComputerGroup>()
            .AsNoTracking()
            .Include(g => g.Members)
            .Include(g => g.Criteria)
            .ToListAsync(cancellationToken);

        info.Groups = groups
            .Where(g => g.Type == DeployComputerGroupType.Static
                ? g.Members.Any(m => m.ComputerId == computerId)
                : DeployGroupCriteriaEvaluator.Matches(computer, g.Criteria))
            .Select(g => new ComputerDeploymentGroup
            {
                GroupId = g.Id,
                Name = g.Name,
                TypeLabel = g.Type == DeployComputerGroupType.Static ? "Groupe statique" : "Groupe dynamique"
            })
            .ToList();

        return info;
    }

    private static bool TargetsComputer(DeploymentTask task, Computer computer)
    {
        foreach (DeploymentTaskTarget target in task.Targets)
        {
            if (target.Type == DeploymentTaskTargetType.Computer)
            {
                if (target.ComputerId == computer.Id)
                {
                    return true;
                }
            }
            else if (target.Group is not null)
            {
                bool matches = target.Group.Type == DeployComputerGroupType.Static
                    ? target.Group.Members.Any(m => m.ComputerId == computer.Id)
                    : DeployGroupCriteriaEvaluator.Matches(computer, target.Group.Criteria);

                if (matches)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string StatusLabel(DeploymentStatus status) => status switch
    {
        DeploymentStatus.Pending => "En attente",
        DeploymentStatus.Running => "En cours",
        DeploymentStatus.Success => "Réussi",
        DeploymentStatus.Error => "En erreur",
        _ => status.ToString()
    };

    private static string StatusBadgeCssClass(DeploymentStatus status) => status switch
    {
        DeploymentStatus.Pending => "bg-secondary",
        DeploymentStatus.Running => "bg-azure",
        DeploymentStatus.Success => "bg-success",
        DeploymentStatus.Error => "bg-danger",
        _ => "bg-secondary"
    };
}
