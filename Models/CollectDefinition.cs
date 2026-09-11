using GlpiNg.Modules.Abstractions.Entities;

namespace GlpiNg.Modules.Deployment.Models;

/// <summary>Type de source pour une définition de collecte (front/collect.php côté GLPI-Inventory).</summary>
public enum CollectType
{
    Registry,
    Wmi,
    FileSearch
}

/// <summary>
/// Définition de collecte de données additionnelles auprès de l'agent (registre Windows, WMI,
/// recherche de fichier), au-delà de l'inventaire standard. Gérées depuis l'interface (CRUD, à
/// l'image de front/collect.form.php) et servies aux agents par l'action <c>getCollectJobs</c> du
/// contrôleur <c>AgentController</c>, qui range les réponses dans <see cref="CollectResult"/>.
///
/// Toutes les collectes actives s'appliquent à tous les agents : le plugin d'origine les cible par
/// tâche, GlpiNg n'a pas de tâche de collecte, et inventer un ciblage que rien ne configure serait
/// moins clair que cette règle-là, qui au moins s'annonce.
/// </summary>
public class CollectDefinition : IEntityScoped
{
    /// <inheritdoc />
    public int? EntityId { get; set; }

    /// <inheritdoc />
    public bool IsRecursive { get; set; }

    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Comment { get; set; }
    public bool Enabled { get; set; } = true;
    public CollectType Type { get; set; } = CollectType.Registry;

    public List<CollectRegistryEntry> RegistryEntries { get; set; } = [];
    public List<CollectWmiEntry> WmiEntries { get; set; } = [];
    public List<CollectFileSearchEntry> FileSearchEntries { get; set; } = [];
}

/// <summary>
/// Valeur rapportée par un agent pour une entrée de collecte, sur un poste donné.
///
/// Une seule table pour les trois natures de collecte, là où le plugin d'origine en tient trois
/// (<c>collects_registries_contents</c>, <c>..._wmis_contents</c>, <c>..._files_contents</c>) :
/// elles ne diffèrent que par le sens qu'on donne à <see cref="Key"/> et <see cref="Value"/> —
/// chemin de registre et donnée lue, propriété WMI et valeur, fichier trouvé et taille. Trois
/// tables identiques au nom près se paieraient à chaque lecture et n'apporteraient rien.
///
/// Le résultat remplace le précédent pour la même entrée : une collecte dit l'état du poste à
/// l'instant où elle a tourné, pas une accumulation. L'historique de ce qui a changé relève de
/// l'historique du poste, pas de cette table.
/// </summary>
public class CollectResult
{
    public int Id { get; set; }

    public int ComputerId { get; set; }

    public int CollectDefinitionId { get; set; }
    public CollectDefinition? CollectDefinition { get; set; }

    /// <summary>Nature de la collecte, recopiée ici : elle survit à la suppression du détail de
    /// l'entrée côté définition, et évite une jointure pour afficher un résultat.</summary>
    public CollectType Type { get; set; }

    /// <summary>Nom de l'entrée interrogée, tel que défini sur la collecte.</summary>
    public required string EntryName { get; set; }

    /// <summary>Ce qui a été demandé : chemin de registre, propriété WMI, fichier trouvé.</summary>
    public string? Key { get; set; }

    /// <summary>Ce qui a été trouvé. Null quand l'agent a répondu sans valeur.</summary>
    public string? Value { get; set; }

    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

public class CollectRegistryEntry
{
    public int Id { get; set; }
    public int CollectDefinitionId { get; set; }
    public required string Name { get; set; }
    public required string Hive { get; set; }
    public required string Path { get; set; }
    public required string RegistryKey { get; set; }
}

public class CollectWmiEntry
{
    public int Id { get; set; }
    public int CollectDefinitionId { get; set; }
    public required string Name { get; set; }
    public string? Moniker { get; set; }
    public required string WmiClass { get; set; }
    public string? Properties { get; set; }
}

public class CollectFileSearchEntry
{
    public int Id { get; set; }
    public int CollectDefinitionId { get; set; }
    public required string Name { get; set; }
    public required string Path { get; set; }
    public string? Pattern { get; set; }
    public bool Recursive { get; set; }
}
