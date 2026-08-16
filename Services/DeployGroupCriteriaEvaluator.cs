using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Inventory.Models;

namespace GlpiNg.Modules.Deployment.Services;

/// <summary>
/// Calcule l'appartenance d'un ordinateur à un groupe dynamique à partir de sa liste de
/// <see cref="DeployComputerGroupCriterion"/>, évaluée en mémoire (voir la remarque sur
/// <see cref="DeployComputerGroup"/> : ce n'est pas le moteur de règles générique de GLPI).
/// </summary>
public static class DeployGroupCriteriaEvaluator
{
    /// <summary>Un groupe dynamique sans aucun critère ne contient aucun ordinateur (comportement le moins surprenant).</summary>
    public static bool Matches(Computer computer, IReadOnlyList<DeployComputerGroupCriterion> criteria)
    {
        if (criteria.Count == 0)
        {
            return false;
        }

        bool? result = null;
        foreach (DeployComputerGroupCriterion criterion in criteria.OrderBy(c => c.SortOrder))
        {
            bool matches = EvaluateSingle(computer, criterion);
            result = result is null
                ? matches
                : criterion.Link == DeployCriterionLink.Or ? result.Value || matches : result.Value && matches;
        }

        return result ?? false;
    }

    public static List<Computer> Filter(IEnumerable<Computer> computers, IReadOnlyList<DeployComputerGroupCriterion> criteria) =>
        computers.Where(c => Matches(c, criteria)).ToList();

    private static bool EvaluateSingle(Computer computer, DeployComputerGroupCriterion criterion)
    {
        string? fieldValue = criterion.Field switch
        {
            DeployCriterionField.Name => computer.Name,
            DeployCriterionField.SerialNumber => computer.SerialNumber,
            DeployCriterionField.Manufacturer => computer.Manufacturer,
            DeployCriterionField.Model => computer.Model,
            DeployCriterionField.OperatingSystem => computer.OperatingSystem,
            DeployCriterionField.OsVersion => computer.OsVersion,
            DeployCriterionField.Status => computer.Status.ToString(),
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
