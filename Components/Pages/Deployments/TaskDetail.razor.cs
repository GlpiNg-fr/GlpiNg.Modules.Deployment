using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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

    // Journal brut de l'agent, ligne par ligne : "[HH:mm:ss] [phase] message". On extrait
    // l'horodatage et la phase pour les styler à part (voir .glpi-log-time/.glpi-log-tag) et on
    // colore la ligne entière selon son issue (ok/succès en vert, ko/erreur en rouge, séparateurs
    // "====" atténués) pour que le regard retrouve immédiatement les étapes en échec dans un long
    // journal. Tout le texte libre passe par HtmlEncode avant d'être réinjecté, la seule structure
    // ajoutée étant les balises <div>/<span> ci-dessous.
    private static readonly Regex LogLinePrefixRegex = new(
        @"^\[(?<time>\d{2}:\d{2}:\d{2})\]\s*(?:\[(?<tag>[a-zA-Z]+)\]\s*)?(?<rest>.*)$",
        RegexOptions.Compiled);

    private static MarkupString RenderLog(string log)
    {
        StringBuilder html = new();

        foreach (string rawLine in log.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            string trimmed = line.Trim();

            string lineClass = trimmed.Length > 0 && trimmed.All(c => c == '=')
                ? "glpi-log-line glpi-log-sep"
                : Regex.IsMatch(line, @"\(ok\)\s*$", RegexOptions.IgnoreCase) || line.Contains("success", StringComparison.OrdinalIgnoreCase)
                    ? "glpi-log-line glpi-log-ok"
                    : Regex.IsMatch(line, @"\(ko\)\s*$", RegexOptions.IgnoreCase)
                      || line.Contains("error", StringComparison.OrdinalIgnoreCase)
                      || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
                        ? "glpi-log-line glpi-log-error"
                        : "glpi-log-line";

            html.Append("<div class=\"").Append(lineClass).Append("\">");

            Match match = LogLinePrefixRegex.Match(line);
            if (match.Success)
            {
                html.Append("<span class=\"glpi-log-time\">[").Append(WebUtility.HtmlEncode(match.Groups["time"].Value)).Append("]</span> ");
                if (match.Groups["tag"].Success)
                {
                    html.Append("<span class=\"glpi-log-tag\">[").Append(WebUtility.HtmlEncode(match.Groups["tag"].Value)).Append("]</span> ");
                }

                html.Append(WebUtility.HtmlEncode(match.Groups["rest"].Value));
            }
            else
            {
                html.Append(WebUtility.HtmlEncode(line));
            }

            html.Append("</div>");
        }

        return new MarkupString(html.ToString());
    }

    // Contenu du bouton "i" à côté des selects de créneau horaire (Créneau horaire de
    // préparation/d'exécution) : aperçu textuel des entrées du créneau sélectionné, faute d'un
    // composant popover dédié.
    private string TimeSlotTooltip(int? timeSlotId)
    {
        if (timeSlotId is not { } id)
        {
            return "Aucun créneau sélectionné.";
        }

        TimeSlot? slot = _availableTimeSlots.FirstOrDefault(s => s.Id == id);
        if (slot is null || slot.Entries.Count == 0)
        {
            return "Ce créneau n'a aucune entrée configurée.";
        }

        return string.Join(", ", slot.Entries
            .OrderBy(e => e.DayOfWeek)
            .ThenBy(e => e.StartTime)
            .Select(e => $"{DayLabel(e.DayOfWeek)} {e.StartTime.ToString("HH:mm")}-{e.EndTime.ToString("HH:mm")}"));
    }

    private static string DayLabel(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Lundi",
        DayOfWeek.Tuesday => "Mardi",
        DayOfWeek.Wednesday => "Mercredi",
        DayOfWeek.Thursday => "Jeudi",
        DayOfWeek.Friday => "Vendredi",
        DayOfWeek.Saturday => "Samedi",
        DayOfWeek.Sunday => "Dimanche",
        _ => day.ToString()
    };

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
