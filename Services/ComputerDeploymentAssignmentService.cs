using GlpiNg.Modules.Abstractions.Deployment;
using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Deployment.Services;

/// <summary>
/// Implémentation de <see cref="IComputerDeploymentAssignmentService"/> : alimente l'onglet
/// "Déploiement de package" de la fiche Ordinateur (module Inventory). Une "assignation" est un
/// <see cref="DeploymentJob"/> directement créé pour l'agent du poste (voir sa doc : il n'existe
/// pas de <c>ComputerId</c> sur ce modèle, le ciblage se fait par <c>AgentId</c>).
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

        int existingCount = await db.Set<DeploymentPackage>()
            .AsNoTracking()
            .CountAsync(p => packageIds.Contains(p.Id), cancellationToken);
        if (existingCount != packageIds.Count)
        {
            return new DeploymentAssignmentResult
            {
                Status = DeploymentAssignmentStatus.PackageNotFound,
                ErrorMessage = "Au moins un des paquets sélectionnés n'existe plus."
            };
        }

        db.Set<DeploymentJob>().AddRange(packageIds.Select(packageId => new DeploymentJob
        {
            AgentId = agentId,
            PackageId = packageId,
            Status = DeploymentStatus.Pending
        }));
        await db.SaveChangesAsync(cancellationToken);

        return new DeploymentAssignmentResult { Status = DeploymentAssignmentStatus.Success };
    }

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
