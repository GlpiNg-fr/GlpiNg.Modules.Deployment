using GlpiNg.Modules.Abstractions.Localization;
﻿using GlpiNg.Modules.Abstractions.Preferences;
using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class DiscoveredDevices : ComponentBase
{
    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    private List<DiscoveredNetworkDevice> _devices = [];
    private DiscoveredDeviceStatus? _statusFilter;
    private string? _message;
    private bool _messageIsError;

    // Forme d'écriture des adresses MAC retenue pour l'utilisateur (préférence personnelle, sinon
    // réglage de l'instance) — voir IUserPreferences.
    [Inject]
    private IUserPreferences UserPreferences { get; set; } = null!;

    private MacAddressFormat _macFormat = MacAddressFormatter.Fallback;

    private string FormatMac(string? mac) => MacAddressFormatter.Format(mac, _macFormat) ?? "—";

    protected override async Task OnInitializedAsync()
    {
        _macFormat = (await UserPreferences.GetAsync()).MacAddressFormat;

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();

        IQueryable<DiscoveredNetworkDevice> query = db.Set<DiscoveredNetworkDevice>()
            .AsNoTracking()
            .Include(d => d.DiscoveredViaNetworkTask)
            .Include(d => d.PromotedNetworkEquipment);

        if (_statusFilter is { } status)
        {
            query = query.Where(d => d.Status == status);
        }

        _devices = await query.OrderByDescending(d => d.LastSeenAt).ToListAsync();
    }

    private async Task OnStatusFilterChanged(ChangeEventArgs e)
    {
        string? value = e.Value as string;
        _statusFilter = string.IsNullOrEmpty(value) ? null : Enum.Parse<DiscoveredDeviceStatus>(value);
        await LoadAsync();
    }

    // "Promouvoir" : crée un NetworkEquipment à partir des informations découvertes — équivalent
    // manuel de la conversion "Actif non géré" -> Matériel réseau côté GLPI-Inventory. Aucune
    // règle automatique ne le fait à la place de l'admin (contrairement à l'import GLPI MySQL, qui
    // est lui aussi une action manuelle déclenchée par l'admin — voir GlpiMySqlImportService) : un
    // équipement découvert peut être un faux positif (IP répondant au ping sans être un vrai actif
    // à suivre), donc jamais promu silencieusement.
    private async Task PromoteAsync(DiscoveredNetworkDevice device)
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        DiscoveredNetworkDevice? tracked = await db.Set<DiscoveredNetworkDevice>().FirstOrDefaultAsync(d => d.Id == device.Id);
        if (tracked is null || tracked.Status != DiscoveredDeviceStatus.New)
        {
            return;
        }

        NetworkEquipment equipment = new()
        {
            Name = device.Hostname ?? device.IpAddress,
            Type = device.GuessedType,
            Comment = device.SysDescr
        };

        db.Set<NetworkEquipment>().Add(equipment);
        await db.SaveChangesAsync();

        tracked.Status = DiscoveredDeviceStatus.Imported;
        tracked.PromotedNetworkEquipmentId = equipment.Id;
        await db.SaveChangesAsync();

        _message = Tr.T("« {0} » ajouté aux matériels réseau.", equipment.Name);
        _messageIsError = false;
        await LoadAsync();
    }

    private async Task IgnoreAsync(DiscoveredNetworkDevice device)
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        DiscoveredNetworkDevice? tracked = await db.Set<DiscoveredNetworkDevice>().FirstOrDefaultAsync(d => d.Id == device.Id);
        if (tracked is null || tracked.Status != DiscoveredDeviceStatus.New)
        {
            return;
        }

        tracked.Status = DiscoveredDeviceStatus.Ignored;
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private static string StatusLabel(DiscoveredDeviceStatus status) => status switch
    {
        DiscoveredDeviceStatus.New => Tr.T("Nouveau"),
        DiscoveredDeviceStatus.Imported => Tr.T("Importé"),
        DiscoveredDeviceStatus.Ignored => Tr.T("Ignoré"),
        _ => status.ToString()
    };

    private static string StatusBadgeClass(DiscoveredDeviceStatus status) => status switch
    {
        DiscoveredDeviceStatus.New => "bg-azure",
        DiscoveredDeviceStatus.Imported => "bg-success",
        DiscoveredDeviceStatus.Ignored => "bg-secondary",
        _ => "bg-secondary"
    };
}
