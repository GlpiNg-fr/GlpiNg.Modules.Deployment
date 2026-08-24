using System.Text.Json;
using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Deployment.Services;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class WakeOnLanTaskDetail : ComponentBase, IAsyncDisposable
{
    [Parameter]
    public int TaskId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private WakeOnLanTaskLaunchService LaunchService { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    private DbContext? _db;
    private WakeOnLanTask? _task;
    private List<DeployComputerGroup> _availableGroups = [];
    private List<Computer> _availableComputers = [];
    private List<GlpiAgent> _availableAgents = [];
    private List<TimeSlot> _availableTimeSlots = [];

    private int _loadedId;
    private bool _isSaving;
    private bool _isLaunching;
    private string _activeTabKey = "main";
    private int? _expandedJobId;
    private string? _launchMessage;
    private bool _launchMessageIsError;

    private WakeOnLanTaskTargetType _targetTypeToAdd = WakeOnLanTaskTargetType.Group;
    private int _groupIdToAdd;
    private int _computerIdToAdd;
    private int _agentIdToAdd;

    private IEnumerable<DeployComputerGroup> GroupsToAdd =>
        _availableGroups.Where(g => _task is not null && !_task.Targets.Any(t => t.Type == WakeOnLanTaskTargetType.Group && t.GroupId == g.Id));

    private IEnumerable<Computer> ComputersToAdd =>
        _availableComputers.Where(c => _task is not null && !_task.Targets.Any(t => t.Type == WakeOnLanTaskTargetType.Computer && t.ComputerId == c.Id));

    private IEnumerable<GlpiAgent> AgentsToAdd =>
        _availableAgents.Where(a => _task is not null && !_task.RelayAgents.Any(actor => actor.AgentId == a.Id));

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

        _availableGroups = await _db.Set<DeployComputerGroup>().AsNoTracking().OrderBy(g => g.Name).ToListAsync();
        _availableComputers = await _db.Set<Computer>().AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        _availableAgents = await _db.Set<GlpiAgent>().AsNoTracking().OrderBy(a => a.AgentName).ToListAsync();
        _availableTimeSlots = await _db.Set<TimeSlot>().AsNoTracking().OrderBy(t => t.Name).ToListAsync();
    }

    // Voir la remarque équivalente sur NetworkTaskDetail.razor.cs.ReloadTaskAsync :
    // WakeOnLanTaskLaunchService écrit LastLaunchedAt et les nouveaux jobs via son propre
    // DbContext, donc l'instance suivie ici ne les verrait pas sans rechargement complet.
    private async Task ReloadTaskAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        _db = await DbFactory.CreateDbContextAsync();
        _task = await _db.Set<WakeOnLanTask>()
            .Include(t => t.Targets).ThenInclude(target => target.Group)
            .Include(t => t.Targets).ThenInclude(target => target.Computer)
            .Include(t => t.RelayAgents).ThenInclude(a => a.Agent)
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

        _db.Set<WakeOnLanTask>().Remove(_task);
        await _db.SaveChangesAsync();
        Nav.NavigateTo("/tools/deployments/wakeonlan");
    }

    private async Task LaunchAsync()
    {
        if (_task is null) return;

        _isLaunching = true;
        _launchMessage = null;
        try
        {
            WakeOnLanTaskLaunchResult result = await LaunchService.LaunchAsync(_task.Id);
            _launchMessage = result.Message;
            _launchMessageIsError = !result.Success;
        }
        finally
        {
            _isLaunching = false;
        }

        await ReloadTaskAsync();
    }

    private async Task AddTargetAsync()
    {
        if (_db is null || _task is null) return;

        WakeOnLanTaskTarget target;
        if (_targetTypeToAdd == WakeOnLanTaskTargetType.Group)
        {
            if (_groupIdToAdd == 0) return;
            if (_task.Targets.Any(t => t.Type == WakeOnLanTaskTargetType.Group && t.GroupId == _groupIdToAdd)) return;

            target = new WakeOnLanTaskTarget { WakeOnLanTaskId = _task.Id, Type = WakeOnLanTaskTargetType.Group, GroupId = _groupIdToAdd };
        }
        else
        {
            if (_computerIdToAdd == 0) return;
            if (_task.Targets.Any(t => t.Type == WakeOnLanTaskTargetType.Computer && t.ComputerId == _computerIdToAdd)) return;

            target = new WakeOnLanTaskTarget { WakeOnLanTaskId = _task.Id, Type = WakeOnLanTaskTargetType.Computer, ComputerId = _computerIdToAdd };
        }

        _task.Targets.Add(target);
        await _db.SaveChangesAsync();

        _groupIdToAdd = 0;
        _computerIdToAdd = 0;
        await _db.Entry(_task).Collection(t => t.Targets).LoadAsync();
        foreach (WakeOnLanTaskTarget existingTarget in _task.Targets)
        {
            await _db.Entry(existingTarget).Reference(t => t.Group).LoadAsync();
            await _db.Entry(existingTarget).Reference(t => t.Computer).LoadAsync();
        }
    }

    private async Task RemoveTargetAsync(WakeOnLanTaskTarget target)
    {
        if (_db is null || _task is null) return;

        _task.Targets.Remove(target);
        _db.Set<WakeOnLanTaskTarget>().Remove(target);
        await _db.SaveChangesAsync();
    }

    private async Task AddActorAsync()
    {
        if (_db is null || _task is null || _agentIdToAdd == 0) return;
        if (_task.RelayAgents.Any(a => a.AgentId == _agentIdToAdd)) return;

        _task.RelayAgents.Add(new WakeOnLanTaskActor { WakeOnLanTaskId = _task.Id, AgentId = _agentIdToAdd });
        await _db.SaveChangesAsync();

        _agentIdToAdd = 0;
        await _db.Entry(_task).Collection(t => t.RelayAgents).LoadAsync();
        foreach (WakeOnLanTaskActor actor in _task.RelayAgents)
        {
            await _db.Entry(actor).Reference(a => a.Agent).LoadAsync();
        }
    }

    private async Task RemoveActorAsync(WakeOnLanTaskActor actor)
    {
        if (_db is null || _task is null) return;

        _task.RelayAgents.Remove(actor);
        _db.Set<WakeOnLanTaskActor>().Remove(actor);
        await _db.SaveChangesAsync();
    }

    private void ToggleLog(int jobId)
    {
        _expandedJobId = _expandedJobId == jobId ? null : jobId;
    }

    // Journal et aperçu de créneau horaire partagés avec TaskDetail.razor.cs/NetworkTaskDetail.razor.cs.
    private static MarkupString RenderLog(string log) => AgentLogFormatter.RenderLog(log);

    private string TimeSlotTooltip(int? timeSlotId) => TimeSlotHelper.Tooltip(_availableTimeSlots, timeSlotId);

    private static string TargetTypeLabel(WakeOnLanTaskTargetType type) =>
        type == WakeOnLanTaskTargetType.Computer ? "Ordinateur" : "Groupe d'ordinateurs";

    /// <summary>Nombre de cibles figées dans le job au lancement — voir la doc de WakeOnLanTaskJob.</summary>
    private static int TargetCount(WakeOnLanTaskJob job)
    {
        try
        {
            return JsonSerializer.Deserialize<List<WakeOnLanTarget>>(job.TargetMacsJson)?.Count ?? 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static string StatusLabel(WakeOnLanJobStatus status) => status switch
    {
        WakeOnLanJobStatus.Pending => "En attente",
        WakeOnLanJobStatus.Running => "En cours",
        WakeOnLanJobStatus.Success => "Réussi",
        WakeOnLanJobStatus.Error => "En erreur",
        _ => status.ToString()
    };

    private static string StatusBadgeClass(WakeOnLanJobStatus status) => status switch
    {
        WakeOnLanJobStatus.Pending => "bg-secondary",
        WakeOnLanJobStatus.Running => "bg-azure",
        WakeOnLanJobStatus.Success => "bg-success",
        WakeOnLanJobStatus.Error => "bg-danger",
        _ => "bg-secondary"
    };

    // Miroir des vérifications de WakeOnLanTaskLaunchService.LaunchAsync.
    private static string? LaunchDisabledReason(WakeOnLanTask task)
    {
        if (!task.IsActive) return "Cette tâche est désactivée.";
        if (task.Targets.Count == 0) return "Aucune cible configurée.";
        if (task.RelayAgents.Count == 0) return "Aucun agent relais configuré.";

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
