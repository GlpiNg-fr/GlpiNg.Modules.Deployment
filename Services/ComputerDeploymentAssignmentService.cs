using GlpiNg.Modules.Abstractions.Deployment;
using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Deployment.Services;

/// <summary>
/// Implémentation de <see cref="IComputerDeploymentAssignmentService"/> : alimente l'onglet
/// "Déploiement de package" de la fiche Ordinateur (module Inventory). Une "assignation" est un
/// <see cref="DeploymentJob"/> directement créé pour l'agent du poste (voir sa doc : il n'existe
/// pas de <c>ComputerId</c> sur ce modèle, le ciblage se fait par <c>AgentId</c>) — le déploiement
/// reste immédiat, sans passer par les vérifications de relance de
/// <see cref="DeploymentTaskLaunchService"/> (ce n'est pas un "lancement" au sens de cette
/// dernière, juste une assignation ponctuelle). Chaque assignation est cependant rattachée à une
/// <see cref="DeploymentTask"/> "à la demande" nommée "[On Demand] {Nom du paquet}" (créée au
/// besoin, voir <see cref="GetOrCreateOnDemandTaskAsync"/>) plutôt qu'un <see cref="DeploymentJob"/>
/// orphelin : ça fait apparaître l'assignation dans /tools/deployments/tasks et dans l'onglet
/// "Tâches / groupes" des autres postes déjà assignés au même paquet, et une seule tâche
/// accumule tous les postes assignés à ce paquet au fil du temps (un acteur Ordinateur par poste).
/// </summary>
public sealed class ComputerDeploymentAssignmentService(IDbContextFactory<DbContext> dbFactory) : IComputerDeploymentAssignmentService
{
    public async Task<List<DeploymentPackageOption>> GetAvailablePackagesAsync(CancellationToken cancellationToken = default)
    {
        await using DbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.Set<DeploymentPackage>()
            .AsNoTracking()
            .Where(p => p.SupersededByPackageId == null)
            .OrderBy(p => p.Name)
            .Select(p => new DeploymentPackageOption { Id = p.Id, Name = p.Name })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<ComputerDeploymentAssignment>> GetAssignmentsAsync(int computerId, CancellationToken cancellationToken = default)
    {
        await using DbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        Computer? computer = await db.Set<Computer>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken);

        if (computer?.AgentId is not { } agentId)
        {
            return [];
        }

        List<DeploymentJob> jobs = await db.Set<DeploymentJob>()
            .AsNoTracking()
            .Include(j => j.Package)
            .Where(j => j.AgentId == agentId)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(cancellationToken);

        return jobs
            .Where(j => j.Package is not null)
            .Select(j => new ComputerDeploymentAssignment
            {
                JobId = j.Id,
                PackageId = j.PackageId,
                PackageName = j.Package!.Name,
                StatusLabel = StatusLabel(j.Status),
                StatusBadgeCssClass = StatusBadgeCssClass(j.Status),
                CanCancel = j.Status == DeploymentStatus.Pending,
                CreatedAtUtc = j.CreatedAt,
                StartedAtUtc = j.StartedAt,
                CompletedAtUtc = j.CompletedAt
            })
            .ToList();
    }

    public async Task<DeploymentAssignmentResult> AssignPackagesAsync(int computerId, IReadOnlyCollection<int> packageIds, CancellationToken cancellationToken = default)
    {
        await using DbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        Computer? computer = await db.Set<Computer>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken);

        if (computer?.AgentId is not { } agentId)
        {
            return new DeploymentAssignmentResult
            {
                Status = DeploymentAssignmentStatus.NoAgent,
                ErrorMessage = "Ce poste n'est associé à aucun agent GLPI-Agent."
            };
        }

        List<DeploymentPackage> packages = await db.Set<DeploymentPackage>()
            .Where(p => packageIds.Contains(p.Id))
            .ToListAsync(cancellationToken);
        if (packages.Count != packageIds.Count)
        {
            return new DeploymentAssignmentResult
            {
                Status = DeploymentAssignmentStatus.PackageNotFound,
                ErrorMessage = "Au moins un des paquets sélectionnés n'existe plus."
            };
        }

        foreach (DeploymentPackage package in packages)
        {
            DeploymentTask onDemandTask = await GetOrCreateOnDemandTaskAsync(db, package, computerId, cancellationToken);

            db.Set<DeploymentJob>().Add(new DeploymentJob
            {
                AgentId = agentId,
                PackageId = package.Id,
                Task = onDemandTask,
                Status = DeploymentStatus.Pending
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return new DeploymentAssignmentResult { Status = DeploymentAssignmentStatus.Success };
    }

    /// <summary>
    /// Retrouve (par nom exact) ou crée la DeploymentTask "à la demande" associée à
    /// <paramref name="package"/> et s'assure que <paramref name="computerId"/> y figure comme
    /// acteur (ajouté s'il n'y est pas déjà) — voir la doc de la classe. Le paquet et cet acteur
    /// sont ajoutés à une entité déjà suivie par <paramref name="db"/> (tâche nouvellement créée
    /// ou rechargée avec Include ci-dessous) : pas de SaveChanges ici, le tout est persisté par
    /// l'appelant dans le même SaveChangesAsync que le DeploymentJob créé pour cette assignation.
    /// </summary>
    private static async Task<DeploymentTask> GetOrCreateOnDemandTaskAsync(
        DbContext db, DeploymentPackage package, int computerId, CancellationToken cancellationToken)
    {
        string taskName = OnDemandTaskName(package.Name);

        DeploymentTask? task = await db.Set<DeploymentTask>()
            .Include(t => t.Packages)
            .Include(t => t.Targets)
            .FirstOrDefaultAsync(t => t.Name == taskName, cancellationToken);

        if (task is null)
        {
            task = new DeploymentTask { Name = taskName, IsActive = true };
            task.Packages.Add(new DeploymentTaskPackage { Package = package });
            task.Targets.Add(new DeploymentTaskTarget { Type = DeploymentTaskTargetType.Computer, ComputerId = computerId });
            db.Set<DeploymentTask>().Add(task);
            return task;
        }

        if (!task.Packages.Any(p => p.PackageId == package.Id))
        {
            task.Packages.Add(new DeploymentTaskPackage { Package = package });
        }

        if (!task.Targets.Any(t => t.Type == DeploymentTaskTargetType.Computer && t.ComputerId == computerId))
        {
            task.Targets.Add(new DeploymentTaskTarget { Type = DeploymentTaskTargetType.Computer, ComputerId = computerId });
        }

        return task;
    }

    private static string OnDemandTaskName(string packageName) => $"[On Demand] {packageName}";

    public async Task<bool> CancelAssignmentAsync(int jobId, CancellationToken cancellationToken = default)
    {
        await using DbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        DeploymentJob? job = await db.Set<DeploymentJob>()
            .FirstOrDefaultAsync(j => j.Id == jobId && j.Status == DeploymentStatus.Pending, cancellationToken);
        if (job is null)
        {
            return false;
        }

        db.Set<DeploymentJob>().Remove(job);
        await db.SaveChangesAsync(cancellationToken);
        return true;
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
