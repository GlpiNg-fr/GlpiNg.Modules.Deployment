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
/// conserver l'historique — voir <see cref="TargetsComputer(List{DeploymentTaskTarget}, Computer)"/>.
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
                .Include(j => j.Package)
                .Where(j => j.AgentId == agentId && j.TaskId != null)
                .ToListAsync(cancellationToken);

            jobsByTaskId = agentJobs
                .GroupBy(j => j.TaskId!.Value)
                .ToDictionary(group => group.Key, group => group.ToList());
        }

        List<ComputerDeploymentTask> deploymentTaskEntries = tasks
            .Where(task => TargetsComputer(task.Targets, computer) || jobsByTaskId.ContainsKey(task.Id))
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
                        Key = $"deploy-{j.Id}",
                        PackageName = j.Package?.Name,
                        Log = j.Log,
                        DateUtc = j.StartedAt ?? j.CreatedAt,
                        StatusLabel = StatusLabel(j.Status),
                        StatusBadgeCssClass = StatusBadgeCssClass(j.Status)
                    })
                    .ToList()
            })
            .ToList();

        // WakeOnLanTask cible des ordinateurs/groupes exactement comme DeploymentTask (contrairement
        // à NetworkTask/EsxTask/CollectTask, dont les acteurs sont des agents et qui restent donc
        // hors de cet onglet) — voir la doc de WakeOnLanTask.
        List<WakeOnLanTask> wakeOnLanTasks = await db.Set<WakeOnLanTask>()
            .AsNoTracking()
            .Include(t => t.Targets).ThenInclude(target => target.Group).ThenInclude(g => g!.Members)
            .Include(t => t.Targets).ThenInclude(target => target.Group).ThenInclude(g => g!.Criteria)
            .ToListAsync(cancellationToken);

        Dictionary<int, List<WakeOnLanTaskJob>> wakeOnLanJobsByTaskId = [];
        if (computer.AgentId is not null)
        {
            // Les jobs WakeOnLan référencent l'agent RELAIS (voir la doc de WakeOnLanTaskJob), pas
            // l'agent de l'ordinateur réveillé — retrouver les exécutions concernant ce poste passe
            // donc par les cibles figées dans TargetMacsJson, pas par un filtre AgentId comme pour
            // DeploymentJob ci-dessus.
            List<int> wakeOnLanTaskIds = wakeOnLanTasks
                .Where(task => TargetsComputer(task.Targets, computer))
                .Select(task => task.Id)
                .ToList();

            if (wakeOnLanTaskIds.Count > 0)
            {
                List<WakeOnLanTaskJob> candidateJobs = await db.Set<WakeOnLanTaskJob>()
                    .AsNoTracking()
                    .Where(j => wakeOnLanTaskIds.Contains(j.WakeOnLanTaskId))
                    .ToListAsync(cancellationToken);

                // Filtrage en mémoire plutôt qu'en SQL : TargetMacsJson est un blob JSON opaque côté
                // base (voir la doc de WakeOnLanTaskJob), et ce provider doit rester portable entre
                // les fournisseurs supportés (SQL Server/MySQL/Postgres, voir CLAUDE.md) sans recourir
                // à des fonctions JSON spécifiques à l'un d'eux.
                wakeOnLanJobsByTaskId = candidateJobs
                    .Where(j => JobTargetsComputer(j, computer.Id))
                    .GroupBy(j => j.WakeOnLanTaskId)
                    .ToDictionary(group => group.Key, group => group.ToList());
            }
        }

        List<ComputerDeploymentTask> wakeOnLanTaskEntries = wakeOnLanTasks
            .Where(task => TargetsComputer(task.Targets, computer) || wakeOnLanJobsByTaskId.ContainsKey(task.Id))
            .Select(task => new ComputerDeploymentTask
            {
                TaskId = task.Id,
                Name = task.Name,
                Active = task.IsActive,
                MethodLabel = "Réveil réseau (WakeOnLan)",
                Executions = (wakeOnLanJobsByTaskId.TryGetValue(task.Id, out List<WakeOnLanTaskJob>? jobs) ? jobs : [])
                    .OrderByDescending(j => j.StartedAt ?? j.CreatedAt)
                    .Select(j => new ComputerDeploymentTaskExecution
                    {
                        Key = $"wol-{j.Id}",
                        Log = j.Log,
                        DateUtc = j.StartedAt ?? j.CreatedAt,
                        StatusLabel = WakeOnLanStatusLabel(j.Status),
                        StatusBadgeCssClass = WakeOnLanStatusBadgeCssClass(j.Status)
                    })
                    .ToList()
            })
            .ToList();

        info.Tasks = deploymentTaskEntries
            .Concat(wakeOnLanTaskEntries)
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

    private static bool TargetsComputer(List<DeploymentTaskTarget> targets, Computer computer)
    {
        foreach (DeploymentTaskTarget target in targets)
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

    /// <summary>Même logique que <see cref="TargetsComputer(List{DeploymentTaskTarget}, Computer)"/>
    /// pour une <see cref="WakeOnLanTask"/> — dupliqué plutôt que généralisé sur une interface
    /// commune, cohérent avec le reste du module (voir p. ex. <see cref="NetworkJobStatus"/>
    /// dupliqué de <see cref="DeploymentStatus"/>).</summary>
    private static bool TargetsComputer(List<WakeOnLanTaskTarget> targets, Computer computer)
    {
        foreach (WakeOnLanTaskTarget target in targets)
        {
            if (target.Type == WakeOnLanTaskTargetType.Computer)
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

    /// <summary>Un <see cref="WakeOnLanTaskJob"/> concerne un ordinateur si celui-ci figure parmi
    /// les cibles figées dans <see cref="WakeOnLanTaskJob.TargetMacsJson"/> au lancement — voir la
    /// doc de cette classe pour pourquoi ce n'est pas un simple filtre AgentId comme pour
    /// <see cref="DeploymentJob"/> (le job référence l'agent RELAIS, pas l'ordinateur réveillé).</summary>
    private static bool JobTargetsComputer(WakeOnLanTaskJob job, int computerId)
    {
        try
        {
            List<WakeOnLanTarget>? targets = System.Text.Json.JsonSerializer.Deserialize<List<WakeOnLanTarget>>(job.TargetMacsJson);
            return targets?.Any(t => t.ComputerId == computerId) ?? false;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
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

    private static string WakeOnLanStatusLabel(WakeOnLanJobStatus status) => status switch
    {
        WakeOnLanJobStatus.Pending => "En attente",
        WakeOnLanJobStatus.Running => "En cours",
        WakeOnLanJobStatus.Success => "Réussi",
        WakeOnLanJobStatus.Error => "En erreur",
        _ => status.ToString()
    };

    private static string WakeOnLanStatusBadgeCssClass(WakeOnLanJobStatus status) => status switch
    {
        WakeOnLanJobStatus.Pending => "bg-secondary",
        WakeOnLanJobStatus.Running => "bg-azure",
        WakeOnLanJobStatus.Success => "bg-success",
        WakeOnLanJobStatus.Error => "bg-danger",
        _ => "bg-secondary"
    };
}
