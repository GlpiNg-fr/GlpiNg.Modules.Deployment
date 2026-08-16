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
/// recherche de fichier), au-delà de l'inventaire standard. GlpiNg stocke ces définitions pour
/// leur gestion (CRUD, à l'image de front/collect.form.php) mais ne les transmet pas encore à
/// l'agent via le protocole /glpi-agent — la collecte de "additional-content" n'est pas
/// implémentée côté <c>AgentController</c>, voir la remarque d'adaptation de ce contrôleur.
/// </summary>
public class CollectDefinition
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Comment { get; set; }
    public bool Enabled { get; set; } = true;
    public CollectType Type { get; set; } = CollectType.Registry;

    public List<CollectRegistryEntry> RegistryEntries { get; set; } = [];
    public List<CollectWmiEntry> WmiEntries { get; set; } = [];
    public List<CollectFileSearchEntry> FileSearchEntries { get; set; } = [];
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
