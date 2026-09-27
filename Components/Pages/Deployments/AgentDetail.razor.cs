using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using GlpiNg.Modules.Abstractions.Localization;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class AgentDetail : ComponentBase, IAsyncDisposable
{
    private sealed record FicheTab(string Key, string Icon, string Label, int? Count);

    [Parameter]
    public int AgentId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    private DbContext? _db;
    private GlpiAgent? _agent;
    private List<Computer> _availableComputers = [];
    private List<FicheTab> _tabs = [];
    private string _activeTabKey = "agent";
    private bool _isSaving;
    private int _position;
    private int _total;
    private int? _previousId;
    private int? _nextId;
    private int _computerIdToLink;
    private int _loadedAgentId;

    // OnParametersSetAsync (pas OnInitializedAsync) : en navigation via les boutons
    // précédent/suivant, le routeur Blazor réutilise la même instance de composant et ne fait
    // que changer AgentId — OnInitializedAsync ne se redéclencherait donc jamais.
    protected override async Task OnParametersSetAsync()
    {
        if (_agent is not null && _loadedAgentId == AgentId)
        {
            return;
        }

        _loadedAgentId = AgentId;
        _activeTabKey = "agent";
        _computerIdToLink = 0;

        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        _db = await DbFactory.CreateDbContextAsync();

        // Pas de AsNoTracking : Computer.AgentId est nullable et sa suppression (DeleteAsync)
        // s'appuie sur le comportement ClientSetNull par défaut d'EF Core, qui exige que
        // l'ordinateur lié soit chargé et suivi par le change tracker.
        _agent = await _db.Set<GlpiAgent>()
            .Include(agent => agent.Computer)
            .FirstOrDefaultAsync(agent => agent.Id == AgentId);

        if (_agent is null)
        {
            return;
        }

        _availableComputers = await _db.Set<Computer>()
            .AsNoTracking()
            .Where(computer => computer.AgentId == null)
            .OrderBy(computer => computer.Name)
            .ToListAsync();

        RebuildTabs();

        _total = await _db.Set<GlpiAgent>().AsNoTracking().CountAsync();
        _position = await _db.Set<GlpiAgent>().AsNoTracking().CountAsync(agent => agent.Id <= AgentId);
        _previousId = await _db.Set<GlpiAgent>().AsNoTracking()
            .Where(agent => agent.Id < AgentId)
            .OrderByDescending(agent => agent.Id)
            .Select(agent => (int?)agent.Id)
            .FirstOrDefaultAsync();
        _nextId = await _db.Set<GlpiAgent>().AsNoTracking()
            .Where(agent => agent.Id > AgentId)
            .OrderBy(agent => agent.Id)
            .Select(agent => (int?)agent.Id)
            .FirstOrDefaultAsync();
    }

    private void RebuildTabs()
    {
        if (_agent is null)
        {
            return;
        }

        _tabs =
        [
            new("agent", "ti-cpu", "Agent", null),
            new("importinfo", "ti-file-import", "Informations d'import", null),
            new("tasks", "ti-list-check", "Modules des agents", _agent.InstalledTasks.Length),
        ];
    }

    private void SetTab(string key)
    {
        _activeTabKey = key;
    }

    private async Task SaveAsync()
    {
        if (_db is null || _agent is null)
        {
            return;
        }

        _isSaving = true;

        try
        {
            await _db.SaveChangesAsync();
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task DeleteAsync()
    {
        if (_db is null || _agent is null)
        {
            return;
        }

        _db.Set<GlpiAgent>().Remove(_agent);
        await _db.SaveChangesAsync();

        Nav.NavigateTo("/tools/deployments/agent");
    }

    private async Task LinkComputerAsync()
    {
        if (_db is null || _agent is null || _computerIdToLink == 0)
        {
            return;
        }

        Computer? computer = await _db.Set<Computer>().FirstOrDefaultAsync(c => c.Id == _computerIdToLink);
        if (computer is null)
        {
            return;
        }

        computer.AgentId = _agent.Id;
        await _db.SaveChangesAsync();

        await _db.Entry(_agent).Reference(a => a.Computer).LoadAsync();
        _computerIdToLink = 0;
        _availableComputers = _availableComputers.Where(c => c.Id != computer.Id).ToList();
    }

    private async Task UnlinkComputerAsync()
    {
        if (_db is null || _agent?.Computer is not { } computer)
        {
            return;
        }

        computer.AgentId = null;
        await _db.SaveChangesAsync();

        await _db.Entry(_agent).Reference(a => a.Computer).LoadAsync();
        _availableComputers.Add(computer);
        _availableComputers = _availableComputers.OrderBy(c => c.Name).ToList();
    }

    private static string AgentDisplayName(GlpiAgent agent) =>
        agent.AgentName ?? agent.DeviceId ?? agent.AgentUuid;

    private static string TaskLabel(string task) => task.ToLowerInvariant() switch
    {
        "inventory" => Tr.T("Inventaire d'ordinateur"),
        "netdiscovery" => Tr.T("Découverte réseau (SNMP)"),
        "netinventory" => Tr.T("Inventaire réseau (SNMP)"),
        "wakeonlan" => Tr.T("Wake on LAN"),
        "deploy" => Tr.T("Déploiement de paquets"),
        "collect" => Tr.T("Récolter des données"),
        "remoteinventory" => Tr.T("Inventaire distant"),
        "esx" => Tr.T("Inventaire distant ESX"),
        _ => task
    };

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }
}
