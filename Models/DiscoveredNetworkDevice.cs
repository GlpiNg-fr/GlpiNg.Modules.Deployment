using GlpiNg.Modules.Inventory.Models;

namespace GlpiNg.Modules.Deployment.Models;

public enum DiscoveredDeviceStatus
{
    New,
    Imported,
    Ignored
}

/// <summary>
/// Équipement réseau repéré par une <see cref="NetworkTask"/> (découverte ping/ARP ou inventaire
/// SNMP) mais pas encore rattaché à un actif géré — équivalent de "Actif non géré" de GLPI-Inventory
/// (front/unmanaged.php). Alimenté par Services/NetworkDeviceImportService.cs à réception d'un
/// résultat "netdiscovery"/"netinventory" (voir AgentController.HandleNetworkInventoryAsync).
///
/// Corrélation entre deux scans (voir NetworkDeviceImportService) : par <see cref="MacAddress"/>
/// quand présente (survit à un changement d'IP DHCP), sinon par <see cref="IpAddress"/>.
/// </summary>
public class DiscoveredNetworkDevice
{
    public int Id { get; set; }
    public required string IpAddress { get; set; }
    public string? MacAddress { get; set; }
    public string? Hostname { get; set; }

    /// <summary>SNMP sysDescr — renseigné seulement pour un résultat "netinventory".</summary>
    public string? SysDescr { get; set; }
    public string? SysName { get; set; }
    public string? SysContact { get; set; }
    public string? SysLocation { get; set; }

    /// <summary>Type deviné (best-effort, à partir de SysDescr) — "Switch"/"Routeur"/"Inconnu"...</summary>
    public string? GuessedType { get; set; }

    public int? DiscoveredViaNetworkTaskId { get; set; }
    public NetworkTask? DiscoveredViaNetworkTask { get; set; }

    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public DiscoveredDeviceStatus Status { get; set; } = DiscoveredDeviceStatus.New;

    /// <summary>Renseigné une fois "promu" (bouton "Promouvoir" de DiscoveredDevices.razor) —
    /// équivalent de la conversion "Actif non géré" -> Matériel réseau côté GLPI-Inventory.</summary>
    public int? PromotedNetworkEquipmentId { get; set; }
    public NetworkEquipment? PromotedNetworkEquipment { get; set; }
}
