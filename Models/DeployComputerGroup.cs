using GlpiNg.Modules.Inventory.Models;

namespace GlpiNg.Modules.Deployment.Models;

/// <summary>Type de groupe d'ordinateurs (front/deploygroup.php côté GLPI-Inventory).</summary>
public enum DeployComputerGroupType
{
    Static,
    Dynamic
}

/// <summary>
/// Groupe d'ordinateurs utilisé comme cible de déploiement (distinct de <see cref="GlpiGroup"/>,
/// qui gère les groupes d'utilisateurs). Les groupes statiques ont des membres ajoutés/retirés à la
/// main (<see cref="Members"/>) ; les groupes dynamiques calculent leur appartenance à partir de
/// <see cref="Criteria"/>. GLPI calcule ses groupes dynamiques avec un moteur de règles générique
/// portant sur des dizaines de champs (matériel, logiciel, lieu...) ; GlpiNg n'en reprend qu'un
/// sous-ensemble directement porté par <see cref="Computer"/> (voir DeployCriterionField) — pas de
/// jointure sur composants/logiciels/ports réseau.
/// </summary>
public class DeployComputerGroup
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Comment { get; set; }
    public DeployComputerGroupType Type { get; set; } = DeployComputerGroupType.Static;

    public List<DeployComputerGroupMember> Members { get; set; } = [];
    public List<DeployComputerGroupCriterion> Criteria { get; set; } = [];
}

public class DeployComputerGroupMember
{
    public int Id { get; set; }
    public int DeployComputerGroupId { get; set; }
    public int ComputerId { get; set; }
    public Computer? Computer { get; set; }
}

/// <summary>Champ de <see cref="Computer"/> disponible pour un critère de groupe dynamique.</summary>
public enum DeployCriterionField
{
    Name,
    SerialNumber,
    Manufacturer,
    Model,
    OperatingSystem,
    OsVersion,
    Status,
    Site,
    Building,
    Room,
    AssignedUser
}

public enum DeployCriterionOperator
{
    Contains,
    NotContains,
    Is,
    IsNot,
    IsEmpty
}

/// <summary>Opérateur logique reliant ce critère au résultat cumulé des critères précédents (ignoré pour le premier).</summary>
public enum DeployCriterionLink
{
    And,
    Or
}

public class DeployComputerGroupCriterion
{
    public int Id { get; set; }
    public int DeployComputerGroupId { get; set; }
    public int SortOrder { get; set; }
    public DeployCriterionLink Link { get; set; } = DeployCriterionLink.And;
    public DeployCriterionField Field { get; set; } = DeployCriterionField.Name;
    public DeployCriterionOperator Operator { get; set; } = DeployCriterionOperator.Contains;
    public string? Value { get; set; }
}
