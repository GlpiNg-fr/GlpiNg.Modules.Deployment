using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Inventory.Models;

namespace GlpiNg.Modules.Deployment.Services;

/// <summary>
/// Calcule si un ordinateur correspond à une liste de <see cref="IDeployCriterion"/>, évaluée en
/// mémoire (voir la remarque sur <see cref="DeployComputerGroup"/> : ce n'est pas le moteur de
/// règles générique de GLPI). Généralisé sur l'interface plutôt que sur
/// <see cref="DeployComputerGroupCriterion"/> directement pour être réutilisé tel quel par
/// <see cref="DeploymentRuleCriterion"/> (voir Models/DeploymentRule.cs) — mêmes champ/opérateur/
/// lien, pas de raison de dupliquer cette logique.
/// </summary>
public static class DeployGroupCriteriaEvaluator
{
    /// <summary>Une liste de critères vide ne correspond à aucun ordinateur (comportement le moins surprenant, ex. groupe dynamique sans critère).</summary>
    public static bool Matches<TCriterion>(Computer computer, IReadOnlyList<TCriterion> criteria) where TCriterion : IDeployCriterion
    {
        if (criteria.Count == 0)
        {
            return false;
        }

        bool? result = null;
        foreach (TCriterion criterion in criteria.OrderBy(c => c.SortOrder))
        {
            bool matches = EvaluateSingle(computer, criterion);
            result = result is null
                ? matches
                : criterion.Link == DeployCriterionLink.Or ? result.Value || matches : result.Value && matches;
        }

        return result ?? false;
    }

    public static List<Computer> Filter<TCriterion>(IEnumerable<Computer> computers, IReadOnlyList<TCriterion> criteria) where TCriterion : IDeployCriterion =>
        computers.Where(c => Matches(c, criteria)).ToList();

    private static bool EvaluateSingle(Computer computer, IDeployCriterion criterion)
    {
        string? fieldValue = criterion.Field switch
        {
            DeployCriterionField.Name => computer.Name,
            DeployCriterionField.SerialNumber => computer.SerialNumber,
            DeployCriterionField.Manufacturer => computer.Manufacturer,
            DeployCriterionField.Model => computer.Model,
            DeployCriterionField.OperatingSystem => computer.OperatingSystem,
            DeployCriterionField.OsVersion => computer.OsVersion,
            DeployCriterionField.Status => computer.StatusItem?.Name,
            DeployCriterionField.Site => computer.Site,
            DeployCriterionField.Building => computer.Building,
            DeployCriterionField.Room => computer.Room,
            DeployCriterionField.AssignedUser => computer.AssignedUser,
            _ => null
        };

        return criterion.Operator switch
        {
            DeployCriterionOperator.IsEmpty => string.IsNullOrWhiteSpace(fieldValue),
            DeployCriterionOperator.Contains => Contains(fieldValue, criterion.Value),
            DeployCriterionOperator.NotContains => !Contains(fieldValue, criterion.Value),
            DeployCriterionOperator.Is => string.Equals(fieldValue?.Trim(), criterion.Value?.Trim(), StringComparison.OrdinalIgnoreCase),
            DeployCriterionOperator.IsNot => !string.Equals(fieldValue?.Trim(), criterion.Value?.Trim(), StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool Contains(string? fieldValue, string? needle) =>
        !string.IsNullOrEmpty(needle) && fieldValue is not null && fieldValue.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
