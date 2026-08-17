using GlpiNg.Modules.Abstractions.Deployment;
using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Deployment.Services;

/// <summary>Paquet proposé en libre-service à l'utilisateur connecté (voir SelfService.razor).</summary>
public sealed class SelfServicePackageOption
{
    public int PackageId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
}

/// <summary>Poste sur lequel l'utilisateur connecté peut demander un paquet donné — voir
/// <see cref="SelfServiceDeploymentService.GetEligibleComputersAsync"/>.</summary>
public sealed class SelfServiceComputerOption
{
    public int ComputerId { get; set; }
    public required string Name { get; set; }
}

/// <summary>Une demande de libre-service passée, avec son statut courant.</summary>
public sealed class SelfServiceRequestHistoryEntry
{
    public required string PackageName { get; set; }
    public required string ComputerName { get; set; }
    public required string StatusLabel { get; set; }
    public required string StatusBadgeCssClass { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Alimente la page /self-service : liste les <see cref="DeploymentPackage"/> ouverts au
/// libre-service (<see cref="DeploymentPackage.DeployComputerGroupId"/> renseigné) pour lesquels
/// l'utilisateur connecté figure dans les <see cref="DeploymentPackage.Targets"/> (voir
/// <see cref="IsEligible"/>), résout sur quel(s) poste(s) il peut les demander, et délègue la
/// demande elle-même à <see cref="IComputerDeploymentAssignmentService.AssignPackagesAsync"/> —
/// même mécanique que l'assignation manuelle par un technicien depuis la fiche Ordinateur
/// (création/réutilisation de la DeploymentTask "[On Demand] {paquet}", job immédiat, voir sa
/// doc), simplement déclenchée par l'utilisateur final pour son propre poste plutôt que par un
/// technicien pour n'importe lequel.
///
/// "Son propre poste" est résolu au mieux via <see cref="Computer.LastLoggedUser"/> (dernier
/// utilisateur Windows/etc. connecté, remonté par l'agent à chaque inventaire) comparé au nom de
/// l'utilisateur connecté — voir <see cref="NormalizeUserName"/>. GlpiNg n'a pas de lien fiable
/// "ce poste appartient à cet utilisateur" (<see cref="Computer.AssignedUser"/> existe mais n'est
/// renseigné par aucun import) : un poste dont le dernier utilisateur connecté ne correspond pas
/// exactement n'apparaît pas comme éligible, plutôt que de risquer de proposer à un utilisateur
/// le poste d'un collègue sous prétexte qu'il appartient au même groupe cible.
/// </summary>
public sealed class SelfServiceDeploymentService(
    IDbContextFactory<DbContext> dbFactory,
    ICurrentUserDeploymentContextProvider userContextProvider,
    IComputerDeploymentAssignmentService assignmentService)
{
    private const string OnDemandTaskPrefix = "[On Demand] ";

    public async Task<List<SelfServicePackageOption>> GetAvailablePackagesAsync(int userId, CancellationToken cancellationToken = default)
    {
        CurrentUserDeploymentContext? userContext = await userContextProvider.GetContextAsync(userId, cancellationToken);
        if (userContext is null)
        {
            return [];
        }

        await using DbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        List<DeploymentPackage> packages = await db.Set<DeploymentPackage>()
            .AsNoTracking()
            .Include(p => p.Targets)
            .Where(p => p.DeployComputerGroupId != null && p.SupersededByPackageId == null)
            .ToListAsync(cancellationToken);

        return packages
            .Where(package => IsEligible(package, userContext))
            .OrderBy(package => package.Name)
            .Select(package => new SelfServicePackageOption
            {
                PackageId = package.Id,
                Name = package.Name,
                Description = package.Description
            })
            .ToList();
    }

    public async Task<List<SelfServiceComputerOption>> GetEligibleComputersAsync(int userId, int packageId, CancellationToken cancellationToken = default)
    {
        CurrentUserDeploymentContext? userContext = await userContextProvider.GetContextAsync(userId, cancellationToken);
        if (userContext is null)
        {
            return [];
        }

        await using DbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        DeploymentPackage? package = await db.Set<DeploymentPackage>()
            .AsNoTracking()
            .Include(p => p.Targets)
            .FirstOrDefaultAsync(p => p.Id == packageId, cancellationToken);

        if (package?.DeployComputerGroupId is not { } groupId || !IsEligible(package, userContext))
        {
            return [];
        }

        DeployComputerGroup? group = await db.Set<DeployComputerGroup>()
            .AsNoTracking()
            .Include(g => g.Members)
            .Include(g => g.Criteria)
            .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        if (group is null)
        {
            return [];
        }

        List<Computer> allComputers = await db.Set<Computer>().AsNoTracking().ToListAsync(cancellationToken);

        IEnumerable<Computer> groupComputers = group.Type == DeployComputerGroupType.Static
            ? allComputers.Where(c => group.Members.Any(m => m.ComputerId == c.Id))
            : DeployGroupCriteriaEvaluator.Filter(allComputers, group.Criteria);

        string normalizedUserName = NormalizeUserName(userContext.UserName);

        return groupComputers
            .Where(c => c.AgentId != null && NormalizeUserName(c.LastLoggedUser) == normalizedUserName)
            .OrderBy(c => c.Name)
            .Select(c => new SelfServiceComputerOption { ComputerId = c.Id, Name = c.Name })
            .ToList();
    }

    public async Task<DeploymentAssignmentResult> RequestDeploymentAsync(int userId, int packageId, int computerId, CancellationToken cancellationToken = default)
    {
        List<SelfServiceComputerOption> eligibleComputers = await GetEligibleComputersAsync(userId, packageId, cancellationToken);
        if (!eligibleComputers.Exists(c => c.ComputerId == computerId))
        {
            return new DeploymentAssignmentResult
            {
                Status = DeploymentAssignmentStatus.PackageNotFound,
                ErrorMessage = "Ce paquet n'est pas disponible pour ce poste."
            };
        }

        return await assignmentService.AssignPackagesAsync(computerId, [packageId], cancellationToken);
    }

    /// <summary>Historique des demandes de libre-service pour les postes résolus comme
    /// appartenant à l'utilisateur connecté (voir la doc de la classe) — inclut aussi les
    /// assignations faites par un technicien vers ces mêmes postes, puisqu'elles passent par la
    /// même DeploymentTask "[On Demand] ..." (voir ComputerDeploymentAssignmentService).</summary>
    public async Task<List<SelfServiceRequestHistoryEntry>> GetRequestHistoryAsync(int userId, CancellationToken cancellationToken = default)
    {
        CurrentUserDeploymentContext? userContext = await userContextProvider.GetContextAsync(userId, cancellationToken);
        if (userContext is null)
        {
            return [];
        }

        await using DbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        string normalizedUserName = NormalizeUserName(userContext.UserName);

        List<Computer> myComputers = await db.Set<Computer>()
            .AsNoTracking()
            .Where(c => c.AgentId != null)
            .ToListAsync(cancellationToken);

        Dictionary<int, string> computerNameByAgentId = myComputers
            .Where(c => NormalizeUserName(c.LastLoggedUser) == normalizedUserName)
            .ToDictionary(c => c.AgentId!.Value, c => c.Name);

        if (computerNameByAgentId.Count == 0)
        {
            return [];
        }

        List<DeploymentJob> jobs = await db.Set<DeploymentJob>()
            .AsNoTracking()
            .Include(j => j.Package)
            .Include(j => j.Task)
            .Where(j => computerNameByAgentId.Keys.Contains(j.AgentId) && j.Task != null && j.Task.Name.StartsWith(OnDemandTaskPrefix))
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(cancellationToken);

        return jobs
            .Select(j => new SelfServiceRequestHistoryEntry
            {
                PackageName = j.Package?.Name ?? "—",
                ComputerName = computerNameByAgentId.GetValueOrDefault(j.AgentId, "—"),
                StatusLabel = StatusLabel(j.Status),
                StatusBadgeCssClass = StatusBadgeCssClass(j.Status),
                CreatedAtUtc = j.CreatedAt
            })
            .ToList();
    }

    private static bool IsEligible(DeploymentPackage package, CurrentUserDeploymentContext userContext)
    {
        // Une Cible vide n'est pas "ouvert à tous" mais "pas encore configuré" : évite d'exposer
        // un paquet en libre-service dès que la case DeployComputerGroupId est cochée, avant que
        // l'admin n'ait fini d'ajouter les Cibles (voir la doc de DeploymentPackage.Targets).
        if (package.Targets.Count == 0)
        {
            return false;
        }

        foreach (DeploymentPackageTarget target in package.Targets)
        {
            bool matches = target.Type switch
            {
                DeploymentPackageTargetType.User => target.ItemId == userContext.UserId,
                DeploymentPackageTargetType.Profile => userContext.ProfileIds.Contains(target.ItemId),
                DeploymentPackageTargetType.Group => userContext.GroupIds.Contains(target.ItemId),
                DeploymentPackageTargetType.Entity => userContext.EntityIds.Contains(target.ItemId),
                _ => false
            };

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeUserName(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return string.Empty;
        }

        int backslash = userName.LastIndexOf('\\');
        string simple = backslash >= 0 ? userName[(backslash + 1)..] : userName;
        return simple.Trim().ToLowerInvariant();
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
