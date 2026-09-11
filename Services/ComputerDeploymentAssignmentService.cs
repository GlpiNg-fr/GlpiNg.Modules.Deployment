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
    /// <summary>
    /// Paquets proposables sur la fiche d'un poste : ceux dont le déploiement à la demande est
    /// activé pour un groupe dont ce poste est membre.
    ///
    /// Un paquet sans groupe n'est pas en libre-service — c'est ce que veut dire
    /// <c>DeployComputerGroupId</c> à null (l'option « ----- » du formulaire GLPI-Inventory
    /// d'origine) — et un paquet remplacé ne se propose plus.
    ///
    /// L'appartenance est celle du groupe : liste explicite pour un groupe statique, critères
    /// réévalués sur le poste pour un groupe dynamique. Un groupe dynamique décrit une population
    /// qui change sans qu'on y touche ; s'en remettre à une liste figée le viderait de son sens.
    /// </summary>
    public async Task<List<DeploymentPackageOption>> GetAvailablePackagesAsync(int computerId, CancellationToken cancellationToken = default)
    {
        await using DbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        // StatusItem inclus : les critères dynamiques peuvent porter sur le statut, qui est un
        // intitulé et non une colonne du poste.
        Computer? computer = await db.Set<Computer>()
            .AsNoTracking()
            .Include(c => c.StatusItem)
            .FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken);

        if (computer is null)
        {
            return [];
        }

        List<DeploymentPackage> candidates = await db.Set<DeploymentPackage>()
            .AsNoTracking()
            .Include(p => p.DeployComputerGroup!).ThenInclude(g => g.Members)
            .Include(p => p.DeployComputerGroup!).ThenInclude(g => g.Criteria)
            .Where(p => p.SupersededByPackageId == null && p.DeployComputerGroupId != null)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        return
        [
            .. candidates
                .Where(package => package.DeployComputerGroup is { } group && IsMember(group, computer))
                .Select(package => new DeploymentPackageOption { Id = package.Id, Name = package.Name })
        ];
    }

    private static bool IsMember(DeployComputerGroup group, Computer computer) =>
        group.Type == DeployComputerGroupType.Static
            ? group.Members.Any(member => member.ComputerId == computer.Id)
            : DeployGroupCriteriaEvaluator.Matches(computer, group.Criteria);

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
                TaskId = j.TaskId,
                Log = j.Log,
                PackageName = j.Package!.Name,
                StatusLabel = StatusLabel(j.Status),
                StatusBadgeCssClass = StatusBadgeCssClass(j.Status),
                CanCancel = j.Status == DeploymentStatus.Pending,
                CanRetry = j.Status is DeploymentStatus.Success or DeploymentStatus.Error,
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

        // Le libre-service se vérifie ici aussi, et pas seulement à l'affichage de la liste : un
        // écran resté ouvert pendant qu'on retire un paquet du libre-service enverrait sinon une
        // assignation que plus rien n'autorise. La règle appartient au service, pas à la page.
        HashSet<int> offered = [.. (await GetAvailablePackagesAsync(computerId, cancellationToken)).Select(option => option.Id)];

        if (packageIds.FirstOrDefault(id => !offered.Contains(id)) is var notOffered && notOffered != 0)
        {
            return new DeploymentAssignmentResult
            {
                Status = DeploymentAssignmentStatus.PackageNotFound,
                ErrorMessage = "Au moins un des paquets sélectionnés n'est plus proposé en déploiement à la demande pour ce poste.",
            };
        }

        // Même garde qu'au lancement d'une tâche (voir DeploymentTaskLaunchService) : un paquet dont
        // un fichier n'a aucun fragment n'a pas son contenu, et l'agent échouerait à le télécharger.
        List<string> incompletePackages = await db.Set<DeploymentPackage>()
            .Where(package => packageIds.Contains(package.Id) && package.Files.Any(file => file.Parts.Count == 0))
            .Select(package => package.Name)
            .ToListAsync(cancellationToken);

        if (incompletePackages.Count > 0)
        {
            return new DeploymentAssignmentResult
            {
                Status = DeploymentAssignmentStatus.PackageNotFound,
                ErrorMessage = $"Contenu manquant pour : {string.Join(", ", incompletePackages)}. "
                    + "Téléversez les fichiers de ces paquets depuis leur fiche avant de les assigner."
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

    public async Task<bool> RetryAssignmentAsync(int jobId, CancellationToken cancellationToken = default)
    {
        await using DbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        DeploymentJob? job = await db.Set<DeploymentJob>()
            .FirstOrDefaultAsync(j => j.Id == jobId && (j.Status == DeploymentStatus.Success || j.Status == DeploymentStatus.Error), cancellationToken);
        if (job is null)
        {
            return false;
        }

        job.Status = DeploymentStatus.Pending;
        job.StartedAt = null;
        job.CompletedAt = null;
        // Le journal est cumulatif (voir AgentController.HandleSetStatusAsync, qui ajoute une
        // ligne à chaque appel plutôt que de remplacer) : on le vide pour que le prochain journal
        // ne mélange pas la tentative précédente avec la nouvelle.
        job.Log = null;

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
