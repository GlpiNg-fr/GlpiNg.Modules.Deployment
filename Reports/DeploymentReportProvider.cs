using GlpiNg.Modules.Abstractions.Reports;
using GlpiNg.Modules.Deployment.Models;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Deployment.Reports;

/// <summary>
/// Rapports du module Déploiement, ajoutés à ceux des autres modules sur <c>/tools/reports</c> —
/// voir <see cref="IReportProvider"/>. Ils répondent aux trois questions que pose l'exploitation
/// d'un parc déployé : qu'est-ce qui est passé et qu'est-ce qui a échoué, où en sont les tâches,
/// et que la découverte réseau a-t-elle trouvé qui ne soit pas encore géré.
///
/// Un <c>DeploymentJob</c> n'est pas cloisonné par entité (c'est son paquet qui l'est), d'où la
/// jointure systématique vers <c>DeploymentPackage</c> : elle donne le nom du paquet et, au
/// passage, applique le cloisonnement de l'utilisateur — même principe que la jointure vers
/// <c>Computer</c> côté Inventory.
/// </summary>
public sealed class DeploymentReportProvider(IDbContextFactory<DbContext> dbFactory) : IReportProvider
{
    internal const string DeploymentStatusKey = "deployment-status";
    internal const string DeploymentTasksKey = "deployment-tasks";
    internal const string DiscoveryDevicesKey = "discovery-devices";

    /// <summary>Nombre maximal de lignes détaillées, comme pour les rapports d'inventaire : au-delà,
    /// c'est la page de supervision qu'il faut ouvrir, pas un rapport.</summary>
    private const int DetailRowLimit = 200;

    private const string NotSet = "(non renseigné)";
    private const string TotalLabel = "Total";

    private static readonly ReportDefinition[] Definitions =
    [
        new(DeploymentStatusKey, "État des déploiements",
            "Résultat des jobs de déploiement par paquet, et détail des derniers échecs.",
            "ti-package-export", "Déploiement"),
        new(DeploymentTasksKey, "Tâches de déploiement",
            "Paquets, cibles et résultats de chaque tâche de déploiement.",
            "ti-list-check", "Déploiement"),
        new(DiscoveryDevicesKey, "Équipements découverts",
            "Ce que la découverte réseau a trouvé : par statut, par type, et ce qui reste à traiter.",
            "ti-radar", "Réseau"),
    ];

    public IReadOnlyList<ReportDefinition> GetReports() => Definitions;

    public Task<IReadOnlyList<ReportFilter>> GetFiltersAsync(string reportKey, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ReportFilter>>(reportKey switch
        {
            // Bornes de période sur la date de création du job. Sans défaut : le rapport porte sur
            // tout l'historique tant qu'on ne le restreint pas.
            DeploymentStatusKey => [ReportFilter.Date("from", "Jobs créés depuis le"), ReportFilter.Date("to", "Jobs créés jusqu'au")],
            _ => [],
        });

    public async Task<ReportResult?> RunAsync(string reportKey, ReportParameters parameters, CancellationToken cancellationToken = default)
    {
        if (!Definitions.Any(definition => definition.Key == reportKey))
        {
            return null;
        }

        await using DbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return reportKey switch
        {
            DeploymentStatusKey => await RunDeploymentStatusAsync(db, parameters, cancellationToken),
            DeploymentTasksKey => await RunDeploymentTasksAsync(db, cancellationToken),
            DiscoveryDevicesKey => await RunDiscoveryDevicesAsync(db, cancellationToken),
            _ => null,
        };
    }

    private static async Task<ReportResult> RunDeploymentStatusAsync(DbContext db, ReportParameters parameters, CancellationToken cancellationToken)
    {
        DateTime? from = parameters.GetDate("from");
        DateTime? to = parameters.GetDate("to");

        var jobs = await db.Set<DeploymentJob>().AsNoTracking()
            .Join(db.Set<DeploymentPackage>().AsNoTracking(),
                job => job.PackageId,
                package => package.Id,
                (job, package) => new
                {
                    PackageName = package.Name,
                    job.Status,
                    job.CreatedAt,
                    job.CompletedAt,
                    AgentName = job.Agent != null ? job.Agent.AgentName ?? job.Agent.DeviceId ?? job.Agent.AgentUuid : null,
                    ComputerId = job.Agent != null && job.Agent.Computer != null ? (int?)job.Agent.Computer.Id : null,
                    ComputerName = job.Agent != null && job.Agent.Computer != null ? job.Agent.Computer.Name : null,
                })
            .ToListAsync(cancellationToken);

        // Filtrage en mémoire après projection : les bornes sont saisies en heure locale alors que
        // les dates sont stockées en UTC, et la conversion ne se traduit pas en SQL de façon
        // identique d'un fournisseur à l'autre (SQL Server, MySQL, PostgreSQL).
        var filtered = jobs
            .Where(job => (from is null || job.CreatedAt.ToLocalTime().Date >= from.Value.Date)
                && (to is null || job.CreatedAt.ToLocalTime().Date <= to.Value.Date))
            .ToList();

        int total = filtered.Count;

        List<ReportRow> byPackage = filtered
            .GroupBy(job => job.PackageName)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new ReportRow(
            [
                ReportCell.Of(group.Key),
                ReportCell.Of(group.Count(job => job.Status == DeploymentStatus.Pending)),
                ReportCell.Of(group.Count(job => job.Status == DeploymentStatus.Running)),
                ReportCell.Of(group.Count(job => job.Status == DeploymentStatus.Success)),
                ReportCell.Of(group.Count(job => job.Status == DeploymentStatus.Error)),
                ReportCell.Of(group.Count()),
                ReportCell.Percent(group.Count(job => job.Status == DeploymentStatus.Success), group.Count()),
            ]))
            .ToList();

        if (byPackage.Count > 0)
        {
            byPackage.Add(new ReportRow(
            [
                ReportCell.Of(TotalLabel),
                ReportCell.Of(filtered.Count(job => job.Status == DeploymentStatus.Pending)),
                ReportCell.Of(filtered.Count(job => job.Status == DeploymentStatus.Running)),
                ReportCell.Of(filtered.Count(job => job.Status == DeploymentStatus.Success)),
                ReportCell.Of(filtered.Count(job => job.Status == DeploymentStatus.Error)),
                ReportCell.Of(total),
                ReportCell.Percent(filtered.Count(job => job.Status == DeploymentStatus.Success), total),
            ], IsTotal: true));
        }

        var failures = filtered
            .Where(job => job.Status == DeploymentStatus.Error)
            .OrderByDescending(job => job.CompletedAt ?? job.CreatedAt)
            .ToList();

        List<ReportRow> failureRows = [.. failures
            .Take(DetailRowLimit)
            .Select(job => new ReportRow(
            [
                ReportCell.Of(job.PackageName),
                job.ComputerId is int computerId
                    ? ReportCell.Link(job.ComputerName, $"/parc/computer/{computerId}")
                    : ReportCell.Of(null),
                ReportCell.Of(job.AgentName),
                ReportCell.Date(job.CreatedAt),
                ReportCell.Date(job.CompletedAt),
            ]))];

        return ReportResult.Of(
            new ReportTable("Jobs par paquet",
            [
                new("Paquet"),
                new("En attente", ReportColumnKind.Number),
                new("En cours", ReportColumnKind.Number),
                new("Succès", ReportColumnKind.Number),
                new("Erreur", ReportColumnKind.Number),
                new(TotalLabel, ReportColumnKind.Number),
                new("Taux de succès", ReportColumnKind.Share),
            ], byPackage,
                "Aucun job de déploiement sur la période choisie."),
            new ReportTable(
                failures.Count > DetailRowLimit
                    ? $"Derniers échecs — {DetailRowLimit} plus récents sur {failures.Count}"
                    : "Derniers échecs",
            [
                new("Paquet"),
                new("Poste"),
                new("Agent"),
                new("Créé le"),
                new("Terminé le"),
            ], failureRows,
                "Aucun échec sur la période choisie."));
    }

    private static async Task<ReportResult> RunDeploymentTasksAsync(DbContext db, CancellationToken cancellationToken)
    {
        var tasks = await db.Set<DeploymentTask>().AsNoTracking()
            .Select(task => new
            {
                task.Id,
                task.Name,
                task.IsActive,
                Packages = task.Packages.Count,
                Targets = task.Targets.Count,
            })
            .ToListAsync(cancellationToken);

        // Une seule requête agrégée pour tous les jobs de tâche, recollée en mémoire : autant de
        // requêtes que de tâches serait le même rapport en beaucoup plus lent.
        var jobStats = await db.Set<DeploymentJob>().AsNoTracking()
            .Where(job => job.TaskId != null)
            .GroupBy(job => new { job.TaskId, job.Status })
            .Select(group => new { group.Key.TaskId, group.Key.Status, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var lastRuns = await db.Set<DeploymentJob>().AsNoTracking()
            .Where(job => job.TaskId != null)
            .GroupBy(job => job.TaskId)
            .Select(group => new { TaskId = group.Key, LastCreatedAt = group.Max(job => job.CreatedAt) })
            .ToListAsync(cancellationToken);

        List<ReportRow> rows = [.. tasks
            .OrderBy(task => task.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(task =>
            {
                int Count(DeploymentStatus status) => jobStats
                    .Where(stat => stat.TaskId == task.Id && stat.Status == status)
                    .Sum(stat => stat.Count);

                int jobCount = jobStats.Where(stat => stat.TaskId == task.Id).Sum(stat => stat.Count);
                DateTime? lastRun = lastRuns.FirstOrDefault(run => run.TaskId == task.Id)?.LastCreatedAt;

                return new ReportRow(
                [
                    ReportCell.Link(task.Name, $"/tools/deployments/tasks/{task.Id}"),
                    ReportCell.Of(task.IsActive ? "Oui" : "Non"),
                    ReportCell.Of(task.Packages),
                    ReportCell.Of(task.Targets),
                    ReportCell.Of(jobCount),
                    ReportCell.Of(Count(DeploymentStatus.Success)),
                    ReportCell.Of(Count(DeploymentStatus.Error)),
                    ReportCell.Of(Count(DeploymentStatus.Pending) + Count(DeploymentStatus.Running)),
                    ReportCell.Date(lastRun),
                ]);
            })];

        return ReportResult.Of(new ReportTable("Tâches de déploiement",
        [
            new("Tâche"),
            new("Active"),
            new("Paquets", ReportColumnKind.Number),
            new("Cibles", ReportColumnKind.Number),
            new("Jobs créés", ReportColumnKind.Number),
            new("Succès", ReportColumnKind.Number),
            new("Erreurs", ReportColumnKind.Number),
            new("En cours", ReportColumnKind.Number),
            new("Dernier lancement"),
        ], rows,
            "Aucune tâche de déploiement définie."));
    }

    private static async Task<ReportResult> RunDiscoveryDevicesAsync(DbContext db, CancellationToken cancellationToken)
    {
        var devices = await db.Set<DiscoveredNetworkDevice>().AsNoTracking()
            .Select(device => new
            {
                device.Id,
                device.IpAddress,
                device.MacAddress,
                device.Hostname,
                device.SysName,
                device.GuessedType,
                device.Status,
                device.LastSeenAt,
            })
            .ToListAsync(cancellationToken);

        int total = devices.Count;

        // Les trois statuts sont toujours affichés — « rien d'ignoré » est une information — sauf
        // quand aucune découverte n'a jamais eu lieu : le message d'explication vaut alors mieux
        // que trois zéros.
        List<ReportRow> byStatus = total == 0
            ? []
            : [.. Enum.GetValues<DiscoveredDeviceStatus>()
                .Select(status => new ReportRow(
                [
                    ReportCell.Of(StatusLabel(status)),
                    ReportCell.Of(devices.Count(device => device.Status == status)),
                    ReportCell.Percent(devices.Count(device => device.Status == status), total),
                ]))];

        if (total > 0)
        {
            byStatus.Add(new ReportRow(
            [
                ReportCell.Of(TotalLabel),
                ReportCell.Of(total),
                ReportCell.Percent(total, total),
            ], IsTotal: true));
        }

        List<ReportRow> byType = devices
            .GroupBy(device => string.IsNullOrWhiteSpace(device.GuessedType) ? NotSet : device.GuessedType!.Trim())
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new ReportRow(
            [
                ReportCell.Of(group.Key),
                ReportCell.Of(group.Count()),
                ReportCell.Percent(group.Count(), total),
            ]))
            .ToList();

        var pending = devices
            .Where(device => device.Status == DiscoveredDeviceStatus.New)
            .OrderByDescending(device => device.LastSeenAt)
            .ToList();

        List<ReportRow> pendingRows = [.. pending
            .Take(DetailRowLimit)
            .Select(device => new ReportRow(
            [
                ReportCell.Link(device.IpAddress, "/parc/unmanaged"),
                ReportCell.Of(device.MacAddress),
                ReportCell.Of(device.Hostname ?? device.SysName),
                ReportCell.Of(device.GuessedType),
                ReportCell.Date(device.LastSeenAt),
            ]))];

        return ReportResult.Of(
            new ReportTable("Équipements découverts par statut",
            [
                new("Statut"),
                new("Équipements", ReportColumnKind.Number),
                new("Part", ReportColumnKind.Share),
            ], byStatus,
                "Aucun équipement découvert — lancez une tâche de découverte réseau depuis /tools/deployments."),
            new ReportTable("Par type deviné",
            [
                new("Type"),
                new("Équipements", ReportColumnKind.Number),
                new("Part", ReportColumnKind.Share),
            ], byType,
                "Aucun équipement découvert."),
            new ReportTable(
                pending.Count > DetailRowLimit
                    ? $"Découvertes non traitées — {DetailRowLimit} plus récentes sur {pending.Count}"
                    : "Découvertes non traitées",
            [
                new("Adresse IP"),
                new("Adresse MAC"),
                new("Nom"),
                new("Type deviné"),
                new("Vu le"),
            ], pendingRows,
                "Toutes les découvertes ont été importées ou ignorées."));
    }

    private static string StatusLabel(DiscoveredDeviceStatus status) => status switch
    {
        DiscoveredDeviceStatus.New => "Nouveau",
        DiscoveredDeviceStatus.Imported => "Importé",
        DiscoveredDeviceStatus.Ignored => "Ignoré",
        _ => status.ToString(),
    };
}
