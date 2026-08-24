using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Deployment.Services;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class TaskDetail : ComponentBase, IAsyncDisposable
{
    [Parameter]
    public int TaskId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private DeploymentTaskLaunchService LaunchService { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    private DbContext? _db;
    private DeploymentTask? _task;
    private List<DeploymentPackage> _availablePackages = [];
    private List<DeployComputerGroup> _availableGroups = [];
    private List<Computer> _availableComputers = [];
    private List<TimeSlot> _availableTimeSlots = [];

    private static readonly int[] WakeUpIntervalOptions = [0, 1, 2, 5, 10, 15, 20, 30, 60];
    private static readonly int[] WakeUpCountOptions = [0, 1, 2, 3, 5, 10, 15, 20, 25, 30, 40, 50, 100];
    private int _loadedId;
    private bool _isSaving;
    private bool _isLaunching;
    private string _activeTabKey = "main";
    private int? _expandedJobId;
    private string? _launchMessage;
    private bool _launchMessageIsError;

    private int _packageIdToAdd;
    private DeploymentTaskTargetType _targetTypeToAdd = DeploymentTaskTargetType.Group;
    private int _groupIdToAdd;
    private int _computerIdToAdd;

    private IEnumerable<DeploymentPackage> PackagesToAdd =>
        _availablePackages.Where(p => _task is not null && !_task.Packages.Any(tp => tp.PackageId == p.Id));

    private IEnumerable<DeployComputerGroup> GroupsToAdd =>
        _availableGroups.Where(g => _task is not null && !_task.Targets.Any(t => t.Type == DeploymentTaskTargetType.Group && t.GroupId == g.Id));

    private IEnumerable<Computer> ComputersToAdd =>
        _availableComputers.Where(c => _task is not null && !_task.Targets.Any(t => t.Type == DeploymentTaskTargetType.Computer && t.ComputerId == c.Id));

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

        _availablePackages = await _db.Set<DeploymentPackage>()
            .AsNoTracking()
            .Where(p => p.SupersededByPackageId == null)
            .OrderBy(p => p.Name)
            .ToListAsync();

        _availableGroups = await _db.Set<DeployComputerGroup>()
            .AsNoTracking()
            .OrderBy(g => g.Name)
            .ToListAsync();

        _availableComputers = await _db.Set<Computer>()
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync();

        _availableTimeSlots = await _db.Set<TimeSlot>()
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    // Reconstruit systématiquement un nouveau DbContext plutôt que de recharger l'entité suivie :
    // après un lancement, DeploymentTaskLaunchService écrit LastLaunchedAt et les nouveaux jobs
    // via son propre DbContext (créé depuis le même IDbContextFactory), donc l'instance
    // suivie ici ne les verrait pas sans un rechargement complet depuis la base.
    private async Task ReloadTaskAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        _db = await DbFactory.CreateDbContextAsync();
        _task = await _db.Set<DeploymentTask>()
            .Include(t => t.Packages).ThenInclude(p => p.Package)
            .Include(t => t.Targets).ThenInclude(target => target.Group)
            .Include(t => t.Targets).ThenInclude(target => target.Computer)
            .Include(t => t.Jobs)
            .ThenInclude(j => j.Agent)
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

        _db.Set<DeploymentTask>().Remove(_task);
        await _db.SaveChangesAsync();
        Nav.NavigateTo("/tools/deployments/tasks");
    }

    private async Task LaunchAsync()
    {
        if (_task is null) return;

        _isLaunching = true;
        _launchMessage = null;
        try
        {
            DeploymentTaskLaunchResult result = await LaunchService.LaunchAsync(_task.Id);
            _launchMessage = result.Message;
            _launchMessageIsError = !result.Success;
        }
        finally
        {
            _isLaunching = false;
        }

        await ReloadTaskAsync();
    }

    private async Task AddPackageAsync()
    {
        if (_db is null || _task is null || _packageIdToAdd == 0) return;
        if (_task.Packages.Any(p => p.PackageId == _packageIdToAdd)) return;

        _task.Packages.Add(new DeploymentTaskPackage { DeploymentTaskId = _task.Id, PackageId = _packageIdToAdd });
        await _db.SaveChangesAsync();

        _packageIdToAdd = 0;
        await _db.Entry(_task).Collection(t => t.Packages).LoadAsync();
        foreach (DeploymentTaskPackage package in _task.Packages)
        {
            await _db.Entry(package).Reference(p => p.Package).LoadAsync();
        }
    }

    private async Task RemovePackageAsync(DeploymentTaskPackage package)
    {
        if (_db is null || _task is null) return;

        _task.Packages.Remove(package);
        _db.Set<DeploymentTaskPackage>().Remove(package);
        await _db.SaveChangesAsync();
    }

    private async Task AddTargetAsync()
    {
        if (_db is null || _task is null) return;

        DeploymentTaskTarget target;
        if (_targetTypeToAdd == DeploymentTaskTargetType.Group)
        {
            if (_groupIdToAdd == 0) return;
            if (_task.Targets.Any(t => t.Type == DeploymentTaskTargetType.Group && t.GroupId == _groupIdToAdd)) return;

            target = new DeploymentTaskTarget { DeploymentTaskId = _task.Id, Type = DeploymentTaskTargetType.Group, GroupId = _groupIdToAdd };
        }
        else
        {
            if (_computerIdToAdd == 0) return;
            if (_task.Targets.Any(t => t.Type == DeploymentTaskTargetType.Computer && t.ComputerId == _computerIdToAdd)) return;

            target = new DeploymentTaskTarget { DeploymentTaskId = _task.Id, Type = DeploymentTaskTargetType.Computer, ComputerId = _computerIdToAdd };
        }

        _task.Targets.Add(target);
        await _db.SaveChangesAsync();

        _groupIdToAdd = 0;
        _computerIdToAdd = 0;
        await _db.Entry(_task).Collection(t => t.Targets).LoadAsync();
        foreach (DeploymentTaskTarget existingTarget in _task.Targets)
        {
            await _db.Entry(existingTarget).Reference(t => t.Group).LoadAsync();
            await _db.Entry(existingTarget).Reference(t => t.Computer).LoadAsync();
        }
    }

    private async Task RemoveTargetAsync(DeploymentTaskTarget target)
    {
        if (_db is null || _task is null) return;

        _task.Targets.Remove(target);
        _db.Set<DeploymentTaskTarget>().Remove(target);
        await _db.SaveChangesAsync();
    }

    private void ToggleLog(int jobId)
    {
        _expandedJobId = _expandedJobId == jobId ? null : jobId;
    }

    // Journal et aperçu de créneau horaire : voir AgentLogFormatter.RenderLog / TimeSlotHelper.Tooltip
    // (Services/), extraits d'ici en classes statiques partagées pour être réutilisés par
    // NetworkTaskDetail.razor.cs sans dupliquer ~50 lignes de regex/HTML.
    private static MarkupString RenderLog(string log) => AgentLogFormatter.RenderLog(log);

    private string TimeSlotTooltip(int? timeSlotId) => TimeSlotHelper.Tooltip(_availableTimeSlots, timeSlotId);

    private static string DayLabel(DayOfWeek day) => TimeSlotHelper.DayLabel(day);

    private static string WakeUpIntervalLabel(int minutes) => minutes == 0 ? "Jamais" : $"{minutes} min";

    private static string WakeUpCountLabel(int count) => count == 0 ? "Aucun" : count.ToString();

    private static string TargetTypeLabel(DeploymentTaskTargetType type) =>
        type == DeploymentTaskTargetType.Computer ? "Ordinateur" : "Groupe d'ordinateurs";

    private static string TargetName(DeploymentTaskTarget target) =>
        target.Type == DeploymentTaskTargetType.Computer
            ? target.Computer?.Name ?? "—"
            : target.Group?.Name ?? "—";

    private static string StatusLabel(DeploymentStatus status) => status switch
    {
        DeploymentStatus.Pending => "En attente",
        DeploymentStatus.Running => "En cours",
        DeploymentStatus.Success => "Réussi",
        DeploymentStatus.Error => "En erreur",
        _ => status.ToString()
    };

    private static string StatusBadgeClass(DeploymentStatus status) => status switch
    {
        DeploymentStatus.Pending => "bg-secondary",
        DeploymentStatus.Running => "bg-azure",
        DeploymentStatus.Success => "bg-success",
        DeploymentStatus.Error => "bg-danger",
        _ => "bg-secondary"
    };

    // Miroir des vérifications de DeploymentTaskLaunchService.LaunchAsync, pour désactiver le
    // bouton "Lancer maintenant" côté UI avec une explication plutôt que de laisser l'utilisateur
    // découvrir le refus après coup.
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

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }
}
