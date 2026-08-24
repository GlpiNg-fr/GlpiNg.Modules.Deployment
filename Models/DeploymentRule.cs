namespace GlpiNg.Modules.Deployment.Models;

/// <summary>
/// Règle de déploiement : quand un ordinateur correspond à ses critères (mêmes champ/opérateur/
/// lien que <see cref="DeployComputerGroupCriterion"/>, voir <see cref="IDeployCriterion"/>), les
/// paquets listés dans <see cref="Actions"/> lui sont assignés — via le même mécanisme qu'une
/// assignation manuelle (<see cref="Services.ComputerDeploymentAssignmentService"/> : tâche "à la
/// demande" + <c>DeploymentJob</c>), pas un nouveau canal de déploiement.
///
/// Contrairement à <see cref="DeployComputerGroup"/> (qui ne fait que déterminer une appartenance,
/// consommée seulement quand un humain lance une tâche ou clique sur "Assigner"), une
/// DeploymentRule agit d'elle-même : réévaluée à chaque inventaire GLPI-Agent reçu (voir
/// InventoryImportService, comme ComputerRule côté Inventory) pour assigner automatiquement les
/// nouveaux paquets correspondants, et ré-évaluable à la demande sur le parc existant depuis la
/// page de la règle ("Évaluer maintenant"). Assigner un paquet déjà en place pour un poste ne crée
/// pas de doublon (voir DeploymentRuleEngine/l'appelant, qui exclut les paquets déjà présents pour
/// l'agent avant d'appeler ComputerDeploymentAssignmentService).
///
/// Plusieurs règles peuvent correspondre au même ordinateur : contrairement à
/// ImportAssignmentRule (Inventory, qui s'arrête à la première règle vérifiée car ses actions sont
/// exclusives — un seul lieu, refuser ou pas), assigner des paquets est cumulatif et sans
/// conflit — TOUTES les règles actives correspondantes s'appliquent, comme ComputerRule.
/// </summary>
public class DeploymentRule
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Comment { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Ordre d'affichage uniquement (toutes les règles actives correspondantes s'appliquent, il n'y a pas d'arrêt au premier match) — cohérence avec les autres moteurs de règles du projet.</summary>
    public int SortOrder { get; set; }

    public List<DeploymentRuleCriterion> Criteria { get; set; } = [];
    public List<DeploymentRuleAction> Actions { get; set; } = [];
}

public class DeploymentRuleCriterion : IDeployCriterion
{
    public int Id { get; set; }
    public int DeploymentRuleId { get; set; }
    public int SortOrder { get; set; }
    public DeployCriterionLink Link { get; set; } = DeployCriterionLink.And;
    public DeployCriterionField Field { get; set; } = DeployCriterionField.Name;
    public DeployCriterionOperator Operator { get; set; } = DeployCriterionOperator.Contains;
    public string? Value { get; set; }
}

/// <summary>Un paquet à assigner quand la règle correspond.</summary>
public class DeploymentRuleAction
{
    public int Id { get; set; }
    public int DeploymentRuleId { get; set; }

    public int PackageId { get; set; }
    public DeploymentPackage? Package { get; set; }
}
