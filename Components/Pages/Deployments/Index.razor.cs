using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class Index : ComponentBase
{
    private sealed record StatCard(string Title, string Icon, string BadgeClass, int Value, string? Href);

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    private List<StatCard> _cards = [];
    private int _pendingJobs;
    private int _runningJobs;
    private int _successJobs;
    private int _errorJobs;

    protected override async Task OnInitializedAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();

        // "Inventorié" au sens GLPI-Agent : un ordinateur rattaché à un agent, c'est-à-dire
        // effectivement remonté par le protocole /inventory plutôt que créé/importé autrement.
        int inventoriedComputers = await db.Set<Computer>().CountAsync(c => c.AgentId != null);
        int agentCount = await db.Set<GlpiAgent>().CountAsync();
        int packageCount = await db.Set<DeploymentPackage>().CountAsync();

        _pendingJobs = await db.Set<DeploymentJob>().CountAsync(j => j.Status == DeploymentStatus.Pending);
        _runningJobs = await db.Set<DeploymentJob>().CountAsync(j => j.Status == DeploymentStatus.Running);
        _successJobs = await db.Set<DeploymentJob>().CountAsync(j => j.Status == DeploymentStatus.Success);
        _errorJobs = await db.Set<DeploymentJob>().CountAsync(j => j.Status == DeploymentStatus.Error);

        _cards =
        [
            new("Ordinateurs inventoriés", "ti-device-desktop", "bg-primary", inventoriedComputers, "/parc/computer"),
            new("Agents enregistrés", "ti-cpu", "bg-azure", agentCount, "/tools/deployments/agent"),
            new("Paquets de déploiement", "ti-package", "bg-green", packageCount, "/tools/deployments/packages"),
            new("Tâches de déploiement", "ti-rocket", "bg-orange", _pendingJobs + _runningJobs + _successJobs + _errorJobs, "/tools/deployments/tasks"),
        ];
    }
}
