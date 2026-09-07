using GlpiNg.Modules.Abstractions.Entities;

namespace GlpiNg.Modules.Deployment.Models;

/// <summary>
/// Plage d'adresses IP à scanner par une <see cref="NetworkTask"/> (Découverte réseau/Inventaire
/// réseau SNMP) — reprend "Plages IP" de GLPI-Inventory (plugins/glpiinventory/front/iprange.php).
/// <see cref="StartIp"/>/<see cref="EndIp"/> restent des chaînes "a.b.c.d" : ce serveur ne les
/// parse/itère jamais lui-même, il les transmet telles quelles à l'agent qui effectue le scan
/// (voir Services/NetworkJobJsonBuilder.cs).
/// </summary>
public class IpRange : IEntityScoped
{
    /// <inheritdoc />
    public int? EntityId { get; set; }

    /// <inheritdoc />
    public bool IsRecursive { get; set; }

    public int Id { get; set; }
    public required string Name { get; set; }
    public required string StartIp { get; set; }
    public required string EndIp { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
