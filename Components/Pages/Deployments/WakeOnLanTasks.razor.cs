using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Deployment.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class WakeOnLanTasks : ComponentBase
{
    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    [Inject]
    private WakeOnLanTaskLaunchService LaunchService { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    private List<WakeOnLanTask> _tasks = [];
    private readonly HashSet<int> _selectedIds = [];
    private WakeOnLanTask _newTask = NewBlankTask();
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

        _tasks = await db.Set<WakeOnLanTask>()
            .AsNoTracking()
            .Include(t => t.Targets)
            .Include(t => t.RelayAgents)
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
            foreach (WakeOnLanTask task in _tasks)
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
        List<WakeOnLanTask> toDelete = await db.Set<WakeOnLanTask>()
            .Where(task => _selectedIds.Contains(task.Id))
            .ToListAsync();

        db.Set<WakeOnLanTask>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    // Les cibles/agents relais se configurent ensuite sur la fiche de la tâche (même principe que
    // DeploymentTask/NetworkTask, voir Tasks.razor.cs.CreateTaskAsync) : une fois créée, on y navigue.
    private async Task CreateTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(_newTask.Name))
        {
            return;
        }

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        db.Set<WakeOnLanTask>().Add(_newTask);
        await db.SaveChangesAsync();

        int newTaskId = _newTask.Id;
        _newTask = NewBlankTask();
        await JS.InvokeVoidAsync("glpiNg.hideModal", "newWakeOnLanTaskModal");
        Nav.NavigateTo($"/tools/deployments/wakeonlan/{newTaskId}");
    }

    private async Task LaunchAsync(int taskId)
    {
        _launchingTaskId = taskId;
        _launchMessage = null;
        try
        {
            WakeOnLanTaskLaunchResult result = await LaunchService.LaunchAsync(taskId);
            _launchMessage = result.Message;
            _launchMessageIsError = !result.Success;
        }
        finally
        {
            _launchingTaskId = null;
        }

        await LoadAsync();
    }

    private static WakeOnLanTask NewBlankTask() => new() { Name = string.Empty, IsActive = true };

    private static (int Pending, int Running, int Success, int Error) JobStats(WakeOnLanTask task) => (
        task.Jobs.Count(job => job.Status == WakeOnLanJobStatus.Pending),
        task.Jobs.Count(job => job.Status == WakeOnLanJobStatus.Running),
        task.Jobs.Count(job => job.Status == WakeOnLanJobStatus.Success),
        task.Jobs.Count(job => job.Status == WakeOnLanJobStatus.Error));

    // Miroir des vérifications de WakeOnLanTaskLaunchService.LaunchAsync (sauf la résolution
    // MAC, connue seulement au lancement).
    private static string? LaunchDisabledReason(WakeOnLanTask task)
    {
        if (!task.IsActive) return "Cette tâche est désactivée.";
        if (task.Targets.Count == 0) return "Aucune cible configurée.";
        if (task.RelayAgents.Count == 0) return "Aucun agent relais configuré.";

        return null;
    }
}
