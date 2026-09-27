using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Deployment.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using GlpiNg.Modules.Abstractions.Localization;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class NetworkTasks : ComponentBase
{
    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    [Inject]
    private NetworkTaskLaunchService LaunchService { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    private List<NetworkTask> _tasks = [];
    private readonly HashSet<int> _selectedIds = [];
    private NetworkTask _newTask = NewBlankTask();
    private int? _launchingTaskId;
    private string? _launchMessage;
    private bool _launchMessageIsError;

    private bool AllSelected => _tasks.Count > 0 && _selectedIds.Count == _tasks.Count;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();

        _tasks = await db.Set<NetworkTask>()
            .AsNoTracking()
            .Include(t => t.IpRanges)
            .Include(t => t.Credentials)
            .Include(t => t.Actors)
            .Include(t => t.Jobs)
            .OrderBy(t => t.Name)
            .ToListAsync();

        _selectedIds.Clear();
    }

    private void ToggleSelectAll(bool selectAll)
    {
        _selectedIds.Clear();
        if (selectAll)
        {
            foreach (NetworkTask task in _tasks)
            {
                _selectedIds.Add(task.Id);
            }
        }
    }

    private void ToggleSelect(int taskId, bool selected)
    {
        if (selected) _selectedIds.Add(taskId);
        else _selectedIds.Remove(taskId);
    }

    private async Task DeleteSelectedAsync()
    {
        if (_selectedIds.Count == 0) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        List<NetworkTask> toDelete = await db.Set<NetworkTask>()
            .Where(task => _selectedIds.Contains(task.Id))
            .ToListAsync();

        db.Set<NetworkTask>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    // Les plages IP/identifiants/acteurs se configurent ensuite sur la fiche de la tâche (même
    // principe que DeploymentTask, voir Tasks.razor.cs.CreateTaskAsync) : une fois créée, on y
    // navigue directement.
    private async Task CreateTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(_newTask.Name))
        {
            return;
        }

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        db.Set<NetworkTask>().Add(_newTask);
        await db.SaveChangesAsync();

        int newTaskId = _newTask.Id;
        _newTask = NewBlankTask();
        await JS.InvokeVoidAsync("glping.hideModal", "newNetworkTaskModal");
        Nav.NavigateTo($"/tools/deployments/networktasks/{newTaskId}");
    }

    private async Task LaunchAsync(int taskId)
    {
        _launchingTaskId = taskId;
        _launchMessage = null;
        try
        {
            NetworkTaskLaunchResult result = await LaunchService.LaunchAsync(taskId);
            _launchMessage = result.Message;
            _launchMessageIsError = !result.Success;
        }
        finally
        {
            _launchingTaskId = null;
        }

        await LoadAsync();
    }

    private static NetworkTask NewBlankTask() => new() { Name = string.Empty, IsActive = true };

    private static (int Pending, int Running, int Success, int Error) JobStats(NetworkTask task) => (
        task.Jobs.Count(job => job.Status == NetworkJobStatus.Pending),
        task.Jobs.Count(job => job.Status == NetworkJobStatus.Running),
        task.Jobs.Count(job => job.Status == NetworkJobStatus.Success),
        task.Jobs.Count(job => job.Status == NetworkJobStatus.Error));

    // Miroir des vérifications de NetworkTaskLaunchService.LaunchAsync.
    private static string? LaunchDisabledReason(NetworkTask task)
    {
        if (!task.IsActive) return Tr.T("Cette tâche est désactivée.");
        if (task.IpRanges.Count == 0) return Tr.T("Aucune plage IP configurée.");
        if (task.Method == NetworkTaskMethod.NetworkInventory && task.Credentials.Count == 0) return Tr.T("Aucun identifiant SNMP configuré.");
        if (task.Actors.Count == 0) return Tr.T("Aucun acteur configuré.");

        return null;
    }
}
