using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Deployment.Services;

/// <summary>
/// Détermine les paquets à assigner à un ordinateur d'après les <see cref="DeploymentRule"/>
/// actives dont les critères correspondent (voir <see cref="DeployGroupCriteriaEvaluator"/> pour
/// l'évaluation des critères eux-mêmes). Pure résolution en mémoire : ne crée aucun
/// <c>DeploymentJob</c> — c'est à l'appelant (InventoryImportService pour l'évaluation
/// automatique à l'inventaire, ou DeploymentRuleDetail pour "Évaluer maintenant") d'appeler
/// IComputerDeploymentAssignmentService.AssignPackagesAsync avec le résultat.
/// </summary>
public static class DeploymentRuleEngine
{
    /// <summary>Union des paquets de toutes les règles actives dont les critères correspondent — TOUTES s'appliquent, voir la doc de DeploymentRule.</summary>
    public static HashSet<int> ResolvePackageIds(Computer computer, IReadOnlyList<DeploymentRule> rules) =>
        rules
            .Where(r => r.IsActive && DeployGroupCriteriaEvaluator.Matches(computer, r.Criteria))
            .SelectMany(r => r.Actions.Select(a => a.PackageId))
            .ToHashSet();

    /// <summary>
    /// Paquets que les règles assignent à cet ordinateur mais qui ne lui sont pas déjà assignés
    /// (par une exécution précédente de ce même moteur, ou manuellement) — évite de créer un
    /// DeploymentJob en double à chaque réévaluation (inventaire répété, ou "Évaluer maintenant"
    /// relancé plusieurs fois).
    /// </summary>
    public static async Task<List<int>> ResolveNewPackageIdsAsync(
        DbContext db, Computer computer, int agentId, IReadOnlyList<DeploymentRule> rules, CancellationToken cancellationToken = default)
    {
        HashSet<int> packageIds = ResolvePackageIds(computer, rules);
        if (packageIds.Count == 0)
        {
            return [];
        }

        HashSet<int> alreadyAssigned = await db.Set<DeploymentJob>()
            .Where(j => j.AgentId == agentId && packageIds.Contains(j.PackageId))
            .Select(j => j.PackageId)
            .ToHashSetAsync(cancellationToken);

        return packageIds.Where(id => !alreadyAssigned.Contains(id)).ToList();
    }
}
