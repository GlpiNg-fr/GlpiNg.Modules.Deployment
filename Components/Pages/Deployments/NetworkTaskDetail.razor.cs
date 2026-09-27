using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Deployment.Services;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using GlpiNg.Modules.Abstractions.Localization;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class NetworkTaskDetail : ComponentBase, IAsyncDisposable
{
    [Parameter]
    public int TaskId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private NetworkTaskLaunchService LaunchService { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    private DbContext? _db;
    private NetworkTask? _task;
    private List<IpRange> _availableIpRanges = [];
    private List<SnmpCredential> _availableCredentials = [];
    private List<GlpiAgent> _availableAgents = [];
    private List<TimeSlot> _availableTimeSlots = [];

    private int _loadedId;
    private bool _isSaving;
    private bool _isLaunching;
    private string _activeTabKey = "main";
    private int? _expandedJobId;
    private string? _launchMessage;
    private bool _launchMessageIsError;

    private int _ipRangeIdToAdd;
    private int _credentialIdToAdd;
    private int _agentIdToAdd;

    private IEnumerable<IpRange> IpRangesToAdd =>
        _availableIpRanges.Where(r => _task is not null && !_task.IpRanges.Any(tr => tr.IpRangeId == r.Id));

    private IEnumerable<SnmpCredential> CredentialsToAdd =>
        _availableCredentials.Where(c => _task is not null && !_task.Credentials.Any(tc => tc.SnmpCredentialId == c.Id));

    private IEnumerable<GlpiAgent> AgentsToAdd =>
        _availableAgents.Where(a => _task is not null && !_task.Actors.Any(actor => actor.AgentId == a.Id));

    protected override async Task OnParametersSetAsync()
    {
        if (_task is not null && _loadedId == TaskId)
        {
            return;
        }

        _loadedId = TaskId;
        _activeTabKey = "main";
        _launchMessage = null;

        await ReloadTaskAsync();

        if (_db is null) return;

        _availableIpRanges = await _db.Set<IpRange>().AsNoTracking().OrderBy(r => r.Name).ToListAsync();
        _availableCredentials = await _db.Set<SnmpCredential>().AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        _availableAgents = await _db.Set<GlpiAgent>().AsNoTracking().OrderBy(a => a.AgentName).ToListAsync();
        _availableTimeSlots = await _db.Set<TimeSlot>().AsNoTracking().OrderBy(t => t.Name).ToListAsync();
    }

    // Voir la remarque équivalente sur TaskDetail.razor.cs.ReloadTaskAsync : NetworkTaskLaunchService
    // écrit LastLaunchedAt et les nouveaux jobs via son propre DbContext, donc l'instance suivie ici
    // ne les verrait pas sans rechargement complet.
    private async Task ReloadTaskAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        _db = await DbFactory.CreateDbContextAsync();
        _task = await _db.Set<NetworkTask>()
            .Include(t => t.IpRanges).ThenInclude(r => r.IpRange)
            .Include(t => t.Credentials).ThenInclude(c => c.SnmpCredential)
            .Include(t => t.Actors).ThenInclude(a => a.Agent)
            .Include(t => t.Jobs).ThenInclude(j => j.Agent)
            .FirstOrDefaultAsync(t => t.Id == TaskId);
    }

    private void SetTab(string key) => _activeTabKey = key;

    private async Task SaveAsync()
    {
        if (_db is null || _task is null) return;

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
        if (_db is null || _task is null) return;

        _db.Set<NetworkTask>().Remove(_task);
        await _db.SaveChangesAsync();
        Nav.NavigateTo("/tools/deployments/networktasks");
    }

    private async Task LaunchAsync()
    {
        if (_task is null) return;

        _isLaunching = true;
        _launchMessage = null;
        try
        {
            NetworkTaskLaunchResult result = await LaunchService.LaunchAsync(_task.Id);
            _launchMessage = result.Message;
            _launchMessageIsError = !result.Success;
        }
        finally
        {
            _isLaunching = false;
        }

        await ReloadTaskAsync();
    }

    private async Task AddIpRangeAsync()
    {
        if (_db is null || _task is null || _ipRangeIdToAdd == 0) return;
        if (_task.IpRanges.Any(r => r.IpRangeId == _ipRangeIdToAdd)) return;

        _task.IpRanges.Add(new NetworkTaskIpRange { NetworkTaskId = _task.Id, IpRangeId = _ipRangeIdToAdd });
        await _db.SaveChangesAsync();

        _ipRangeIdToAdd = 0;
        await _db.Entry(_task).Collection(t => t.IpRanges).LoadAsync();
        foreach (NetworkTaskIpRange range in _task.IpRanges)
        {
            await _db.Entry(range).Reference(r => r.IpRange).LoadAsync();
        }
    }

    private async Task RemoveIpRangeAsync(NetworkTaskIpRange range)
    {
        if (_db is null || _task is null) return;

        _task.IpRanges.Remove(range);
        _db.Set<NetworkTaskIpRange>().Remove(range);
        await _db.SaveChangesAsync();
    }

    private async Task AddCredentialAsync()
    {
        if (_db is null || _task is null || _credentialIdToAdd == 0) return;
        if (_task.Credentials.Any(c => c.SnmpCredentialId == _credentialIdToAdd)) return;

        _task.Credentials.Add(new NetworkTaskCredential { NetworkTaskId = _task.Id, SnmpCredentialId = _credentialIdToAdd });
        await _db.SaveChangesAsync();

        _credentialIdToAdd = 0;
        await _db.Entry(_task).Collection(t => t.Credentials).LoadAsync();
        foreach (NetworkTaskCredential credential in _task.Credentials)
        {
            await _db.Entry(credential).Reference(c => c.SnmpCredential).LoadAsync();
        }
    }

    private async Task RemoveCredentialAsync(NetworkTaskCredential credential)
    {
        if (_db is null || _task is null) return;

        _task.Credentials.Remove(credential);
        _db.Set<NetworkTaskCredential>().Remove(credential);
        await _db.SaveChangesAsync();
    }

    private async Task AddActorAsync()
    {
        if (_db is null || _task is null || _agentIdToAdd == 0) return;
        if (_task.Actors.Any(a => a.AgentId == _agentIdToAdd)) return;

        _task.Actors.Add(new NetworkTaskActor { NetworkTaskId = _task.Id, AgentId = _agentIdToAdd });
        await _db.SaveChangesAsync();

        _agentIdToAdd = 0;
        await _db.Entry(_task).Collection(t => t.Actors).LoadAsync();
        foreach (NetworkTaskActor actor in _task.Actors)
        {
            await _db.Entry(actor).Reference(a => a.Agent).LoadAsync();
        }
    }

    private async Task RemoveActorAsync(NetworkTaskActor actor)
    {
        if (_db is null || _task is null) return;

        _task.Actors.Remove(actor);
        _db.Set<NetworkTaskActor>().Remove(actor);
        await _db.SaveChangesAsync();
    }

    private void ToggleLog(int jobId)
    {
        _expandedJobId = _expandedJobId == jobId ? null : jobId;
    }

    // Journal et aperçu de créneau horaire partagés avec TaskDetail.razor.cs — voir
    // AgentLogFormatter.RenderLog / TimeSlotHelper.Tooltip (Services/).
    private static MarkupString RenderLog(string log) => AgentLogFormatter.RenderLog(log);

    private string TimeSlotTooltip(int? timeSlotId) => TimeSlotHelper.Tooltip(_availableTimeSlots, timeSlotId);

    private static string StatusLabel(NetworkJobStatus status) => status switch
    {
        NetworkJobStatus.Pending => Tr.T("En attente"),
        NetworkJobStatus.Running => Tr.T("En cours"),
        NetworkJobStatus.Success => Tr.T("Réussi"),
        NetworkJobStatus.Error => Tr.T("En erreur"),
        _ => status.ToString()
    };

    private static string StatusBadgeClass(NetworkJobStatus status) => status switch
    {
        NetworkJobStatus.Pending => "bg-secondary",
        NetworkJobStatus.Running => "bg-azure",
        NetworkJobStatus.Success => "bg-success",
        NetworkJobStatus.Error => "bg-danger",
        _ => "bg-secondary"
    };

    // Miroir des vérifications de NetworkTaskLaunchService.LaunchAsync.
    private static string? LaunchDisabledReason(NetworkTask task)
    {
        if (!task.IsActive) return Tr.T("Cette tâche est désactivée.");
        if (task.IpRanges.Count == 0) return Tr.T("Aucune plage IP configurée.");
        if (task.Method == NetworkTaskMethod.NetworkInventory && task.Credentials.Count == 0) return Tr.T("Aucun identifiant SNMP configuré.");
        if (task.Actors.Count == 0) return Tr.T("Aucun acteur configuré.");

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }
}
