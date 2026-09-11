using GlpiNg.Modules.Abstractions.Deployment;
using GlpiNg.Modules.Deployment.Models;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Deployment.Services;

/// <summary>
/// Alimente l'onglet « Informations de collecte » d'une fiche Ordinateur — voir
/// <see cref="IComputerCollectProvider"/>.
/// </summary>
public sealed class ComputerCollectProvider(IDbContextFactory<DbContext> dbFactory) : IComputerCollectProvider
{
    public static string TypeLabel(CollectType type) => type switch
    {
        CollectType.Registry => "Registre",
        CollectType.Wmi => "WMI",
        CollectType.FileSearch => "Recherche de fichiers",
        _ => type.ToString(),
    };

    public async Task<ComputerCollectInfo> GetForComputerAsync(int computerId, CancellationToken cancellationToken = default)
    {
        await using DbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        List<CollectResult> results = await db.Set<CollectResult>()
            .AsNoTracking()
            .Include(result => result.CollectDefinition)
            .Where(result => result.ComputerId == computerId)
            .ToListAsync(cancellationToken);

        List<ComputerCollectGroup> groups =
        [
            .. results
                .GroupBy(result => result.CollectDefinitionId)
                .Select(group => new ComputerCollectGroup
                {
                    CollectId = group.Key,
                    Name = group.First().CollectDefinition?.Name ?? $"Collecte #{group.Key}",
                    TypeLabel = TypeLabel(group.First().Type),
                    LastCollectedAt = group.Max(result => result.CollectedAt),
                    Entries =
                    [
                        .. group
                            .OrderBy(result => result.EntryName, StringComparer.CurrentCultureIgnoreCase)
                            .Select(result => new ComputerCollectEntry
                            {
                                EntryName = result.EntryName,
                                Key = result.Key,
                                Value = result.Value,
                                CollectedAt = result.CollectedAt,
                            })
                    ],
                })
                .OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
        ];

        // Collectes actives dont ce poste n'a rien rapporté : l'attente se distingue de l'absence.
        HashSet<int> reported = [.. results.Select(result => result.CollectDefinitionId)];

        List<string> pending = await db.Set<CollectDefinition>()
            .AsNoTracking()
            .Where(collect => collect.Enabled && !reported.Contains(collect.Id))
            .OrderBy(collect => collect.Name)
            .Select(collect => collect.Name)
            .ToListAsync(cancellationToken);

        return new ComputerCollectInfo { Groups = groups, PendingCollects = pending };
    }
}
