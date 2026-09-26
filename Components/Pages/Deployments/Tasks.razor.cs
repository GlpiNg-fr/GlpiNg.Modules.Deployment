using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Deployment.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class Tasks : ComponentBase
{
    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    [Inject]
    private DeploymentTaskLaunchService LaunchService { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    private List<DeploymentTask> _tasks = [];
    private List<DeploymentTask> _filteredTasks = [];
    private string _searchTerm = string.Empty;
    private readonly HashSet<int> _selectedIds = [];
    private DeploymentTask _newTask = NewBlankTask();
    private int? _launchingTaskId;
    private string? _launchMessage;
    private bool _launchMessageIsError;

    private bool AllSelected => _filteredTasks.Count > 0 && _selectedIds.Count == _filteredTasks.Count;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();

        _tasks = await db.Set<DeploymentTask>()
            .AsNoTracking()
            .Include(t => t.Packages).ThenInclude(p => p.Package)
            .Include(t => t.Targets).ThenInclude(target => target.Group)
            .Include(t => t.Targets).ThenInclude(target => target.Computer)
            .Include(t => t.Jobs)
            .OrderBy(t => t.Name)
            .ToListAsync();

        _selectedIds.Clear();
        ApplyFilter();
    }

    private async Task OnRefreshAsync()
    {
        await LoadAsync();
    }

    private void OnSearchChanged(KeyboardEventArgs args)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string term = _searchTerm.Trim();

        IEnumerable<DeploymentTask> query = term.Length == 0
            ? _tasks
            : _tasks.Where(task => MatchesSearch(task, term));

        _filteredTasks = query.OrderBy(task => task.Name).ToList();
        _selectedIds.IntersectWith(_filteredTasks.Select(task => task.Id));
    }

    private void ToggleSelectAll(bool selectAll)
    {
        _selectedIds.Clear();
        if (selectAll)
        {
            foreach (DeploymentTask task in _filteredTasks)
            {
                _selectedIds.Add(task.Id);
            }
        }
    }

    private void ToggleSelect(int taskId, bool selected)
    {
        if (selected)
        {
            _selectedIds.Add(taskId);
        }
        else
        {
            _selectedIds.Remove(taskId);
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (_selectedIds.Count == 0) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        List<DeploymentTask> toDelete = await db.Set<DeploymentTask>()
            .Where(task => _selectedIds.Contains(task.Id))
            .ToListAsync();

        db.Set<DeploymentTask>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    // Le paquet et le groupe cible se configurent ensuite sur la fiche de la tâche (voir la doc
    // de DeploymentTask) : une fois créée, on y navigue directement plutôt que de rester sur la
    // liste, pour enchaîner naturellement sur cette configuration.
    private async Task CreateTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(_newTask.Name))
        {
            return;
        }

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        db.Set<DeploymentTask>().Add(_newTask);
        await db.SaveChangesAsync();

        int newTaskId = _newTask.Id;
        _newTask = NewBlankTask();
        await JS.InvokeVoidAsync("glping.hideModal", "newTaskModal");
        Nav.NavigateTo($"/tools/deployments/tasks/{newTaskId}");
    }

    private async Task LaunchAsync(int taskId)
    {
        _launchingTaskId = taskId;
        _launchMessage = null;
        try
        {
            DeploymentTaskLaunchResult result = await LaunchService.LaunchAsync(taskId);
            _launchMessage = result.Message;
            _launchMessageIsError = !result.Success;
        }
        finally
        {
            _launchingTaskId = null;
        }

        await LoadAsync();
    }

    private static DeploymentTask NewBlankTask() => new() { Name = string.Empty, IsActive = true };

    private static bool MatchesSearch(DeploymentTask task, string term)
    {
        return task.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || (task.Comment?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || task.Packages.Any(p => p.Package?.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || task.Targets.Any(t => TargetName(t).Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string TargetName(DeploymentTaskTarget target) =>
        target.Type == DeploymentTaskTargetType.Computer
            ? target.Computer?.Name ?? "—"
            : target.Group?.Name ?? "—";

    private static (int Pending, int Running, int Success, int Error) JobStats(DeploymentTask task) => (
        task.Jobs.Count(job => job.Status == DeploymentStatus.Pending),
        task.Jobs.Count(job => job.Status == DeploymentStatus.Running),
        task.Jobs.Count(job => job.Status == DeploymentStatus.Success),
        task.Jobs.Count(job => job.Status == DeploymentStatus.Error));

    // Miroir des vérifications de DeploymentTaskLaunchService.LaunchAsync, pour désactiver le
    // bouton "Lancer" côté UI avec une explication plutôt que de laisser l'utilisateur découvrir
    // le refus après coup.
    private static string? LaunchDisabledReason(DeploymentTask task)
    {
        if (!task.IsActive) return "Cette tâche est désactivée.";
        if (task.Packages.Count == 0) return "Aucun paquet configuré.";
        if (task.Targets.Count == 0) return "Aucun acteur configuré.";

        bool hasCompletedRun = task.Jobs.Count > 0 && task.Jobs.All(job => job.Status is DeploymentStatus.Success or DeploymentStatus.Error);
        if (hasCompletedRun && !task.AllowRePreparation)
        {
            return "Déjà exécutée : activez la re-préparation pour la relancer.";
        }

        return null;
    }
}
