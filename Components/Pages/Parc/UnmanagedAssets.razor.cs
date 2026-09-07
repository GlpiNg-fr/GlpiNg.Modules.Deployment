using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Deployment.Components.Pages.Parc;

public partial class UnmanagedAssets : ComponentBase
{
    /// <summary>
    /// Types d'actifs vers lesquels un actif non géré peut être converti. GLPI propose la même
    /// bascule (« convertir en ») plutôt qu'un seul type imposé : la découverte réseau remonte
    /// aussi bien des commutateurs que des imprimantes ou des téléphones IP.
    /// </summary>
    private enum PromotionTarget
    {
        NetworkEquipment,
        Printer,
        Phone,
    }

    private static readonly (PromotionTarget Target, string Label, string Icon)[] PromotionTargets =
    [
        (PromotionTarget.NetworkEquipment, "Matériel réseau", "ti-router"),
        (PromotionTarget.Printer, "Imprimante", "ti-printer"),
        (PromotionTarget.Phone, "Téléphone", "ti-phone"),
    ];

    private static readonly int[] PageSizeOptions = [25, 50, 100, 200, 500];

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    private List<DiscoveredNetworkDevice> _items = [];
    private List<DiscoveredNetworkDevice> _filtered = [];
    private List<DiscoveredNetworkDevice> _paged = [];
    private DiscoveredDeviceStatus? _statusFilter;
    private string _search = string.Empty;
    private string _sortField = "lastseen";
    private bool _sortDescending = true;
    private int _pageSize = 25;
    private int _currentPage = 1;
    private string? _message;
    private bool _messageIsError;

    private int TotalPages => _filtered.Count == 0 ? 1 : (int)Math.Ceiling(_filtered.Count / (double)_pageSize);

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _items = await db.Set<DiscoveredNetworkDevice>()
            .AsNoTracking()
            .Include(d => d.PromotedNetworkEquipment)
            .ToListAsync();

        ApplyFilterAndSort();
    }

    private void OnSearchInput(string? value)
    {
        _search = value ?? string.Empty;
        _currentPage = 1;
        ApplyFilterAndSort();
    }

    private void OnStatusFilterChanged(ChangeEventArgs e)
    {
        string? value = e.Value as string;
        _statusFilter = string.IsNullOrEmpty(value) ? null : Enum.Parse<DiscoveredDeviceStatus>(value);
        _currentPage = 1;
        ApplyFilterAndSort();
    }

    private void SetSort(string field)
    {
        if (_sortField == field)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortField = field;
            _sortDescending = false;
        }

        ApplyFilterAndSort();
    }

    private MarkupString SortIndicator(string field)
    {
        if (_sortField != field) return new MarkupString(string.Empty);
        return new MarkupString($"<i class=\"ti {(_sortDescending ? "ti-caret-up-filled" : "ti-caret-down-filled")}\"></i>");
    }

    private void ApplyFilterAndSort()
    {
        IEnumerable<DiscoveredNetworkDevice> matched = _items;

        if (_statusFilter is { } status)
        {
            matched = matched.Where(device => device.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(_search))
        {
            string term = _search.Trim();
            matched = matched.Where(device =>
                device.IpAddress.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (device.Hostname?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (device.SysName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (device.MacAddress?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (device.GuessedType?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        Func<DiscoveredNetworkDevice, IComparable> keySelector = _sortField switch
        {
            "hostname" => device => device.Hostname ?? device.SysName ?? string.Empty,
            "mac" => device => device.MacAddress ?? string.Empty,
            "type" => device => device.GuessedType ?? string.Empty,
            "status" => device => device.Status.ToString(),
            "lastseen" => device => device.LastSeenAt,
            _ => device => device.IpAddress,
        };

        _filtered = (_sortDescending ? matched.OrderByDescending(keySelector) : matched.OrderBy(keySelector)).ToList();
        ApplyPaging();
    }

    private void ApplyPaging()
    {
        _currentPage = Math.Clamp(_currentPage, 1, TotalPages);
        _paged = _filtered.Skip((_currentPage - 1) * _pageSize).Take(_pageSize).ToList();
    }

    private void SetPageSize(int pageSize)
    {
        if (_pageSize == pageSize) return;
        _pageSize = pageSize;
        _currentPage = 1;
        ApplyPaging();
    }

    private void GoToPage(int page)
    {
        int target = Math.Clamp(page, 1, TotalPages);
        if (target == _currentPage) return;
        _currentPage = target;
        ApplyPaging();
    }

    /// <summary>
    /// Crée l'actif géré correspondant et marque l'actif non géré comme converti. Jamais fait
    /// automatiquement : une adresse qui répond au ping n'est pas forcément un actif à suivre —
    /// même raison que sur la page de supervision de la découverte réseau.
    ///
    /// Seule la conversion en matériel réseau garde le lien de retour
    /// (<see cref="DiscoveredNetworkDevice.PromotedNetworkEquipmentId"/>) : le modèle n'a qu'une
    /// référence, et la faire porter sur les trois types demanderait un couple (type, id) — à
    /// reprendre si d'autres types de conversion sont ajoutés.
    /// </summary>
    private async Task PromoteAsync(DiscoveredNetworkDevice device, PromotionTarget target)
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        DiscoveredNetworkDevice? tracked = await db.Set<DiscoveredNetworkDevice>().FirstOrDefaultAsync(d => d.Id == device.Id);

        if (tracked is null || tracked.Status != DiscoveredDeviceStatus.New)
        {
            _message = "Cet actif a déjà été traité.";
            _messageIsError = true;
            await LoadAsync();
            return;
        }

        string name = device.Hostname ?? device.SysName ?? device.IpAddress;
        string? comment = device.SysDescr;

        switch (target)
        {
            case PromotionTarget.NetworkEquipment:
                NetworkEquipment equipment = new() { Name = name, Type = device.GuessedType, Comment = comment };
                db.Set<NetworkEquipment>().Add(equipment);
                await db.SaveChangesAsync();
                tracked.PromotedNetworkEquipmentId = equipment.Id;
                break;

            case PromotionTarget.Printer:
                db.Set<Printer>().Add(new Printer { Name = name, Type = device.GuessedType, Comment = comment });
                await db.SaveChangesAsync();
                break;

            case PromotionTarget.Phone:
                db.Set<Phone>().Add(new Phone { Name = name, Type = device.GuessedType, Comment = comment });
                await db.SaveChangesAsync();
                break;
        }

        tracked.Status = DiscoveredDeviceStatus.Imported;
        await db.SaveChangesAsync();

        _message = $"« {name} » converti en {PromotionTargets.First(t => t.Target == target).Label.ToLowerInvariant()}.";
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

        _message = null;
        await LoadAsync();
    }

    private static string StatusLabel(DiscoveredDeviceStatus status) => status switch
    {
        DiscoveredDeviceStatus.New => "Nouveau",
        DiscoveredDeviceStatus.Imported => "Converti",
        DiscoveredDeviceStatus.Ignored => "Ignoré",
        _ => status.ToString(),
    };

    private static string StatusBadgeClass(DiscoveredDeviceStatus status) => status switch
    {
        DiscoveredDeviceStatus.New => "bg-azure",
        DiscoveredDeviceStatus.Imported => "bg-success",
        DiscoveredDeviceStatus.Ignored => "bg-secondary",
        _ => "bg-secondary",
    };
}
