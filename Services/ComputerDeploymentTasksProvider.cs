using GlpiNg.Modules.Abstractions.Deployment;
using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Deployment.Services;

/// <summary>
/// Implémentation Web de <see cref="IComputerDeploymentTasksProvider"/> : alimente l'onglet
/// "Tâches / groupes" de la fiche Ordinateur (module Inventory) à partir des
/// <see cref="DeploymentJob"/> exécutés sur l'agent du poste et des
/// <see cref="DeployComputerGroup"/> dont il est membre — reprend l'onglet du même nom de
/// GLPI-Inventory (table plugin_glpiinventory_taskjobstates côté GLPI, sans le concept de
/// "Task" englobant plusieurs jobs planifiés : ici un "Tâche" de l'onglet correspond à un
/// <see cref="DeploymentPackage"/>, seul type de job de déploiement que GlpiNg reprend).
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

        if (computer.AgentId is { } agentId)
        {
            List<DeploymentJob> jobs = await db.Set<DeploymentJob>()
                .AsNoTracking()
                .Include(j => j.Package)
                .Where(j => j.AgentId == agentId)
                .ToListAsync(cancellationToken);

            info.Tasks = jobs
                .Where(j => j.Package is not null)
                .GroupBy(j => j.Package!.Id)
                .Select(group => new ComputerDeploymentTask
                {
                    PackageId = group.Key,
                    Name = group.First().Package!.Name,
                    Active = group.First().Package!.SupersededByPackageId is null,
                    MethodLabel = "Déploiement de package",
                    Executions = group
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
                .ToList();
        }

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
