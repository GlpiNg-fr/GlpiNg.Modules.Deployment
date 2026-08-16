using GlpiNg.Modules.Deployment.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class MirrorServers : ComponentBase
{
    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private List<DeploymentMirrorServer> _mirrorServers = [];
    private readonly HashSet<int> _selectedIds = [];
    private DeploymentMirrorServer _newMirrorServer = new() { Name = string.Empty, Url = string.Empty };

    private bool AllSelected => _mirrorServers.Count > 0 && _selectedIds.Count == _mirrorServers.Count;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _mirrorServers = await db.Set<DeploymentMirrorServer>().AsNoTracking().OrderBy(s => s.Name).ToListAsync();
        _selectedIds.Clear();
    }

    private void ToggleSelectAll(bool selectAll)
    {
        _selectedIds.Clear();
        if (selectAll)
        {
            foreach (DeploymentMirrorServer server in _mirrorServers)
            {
                _selectedIds.Add(server.Id);
            }
        }
    }

    private void ToggleSelect(int id, bool selected)
    {
        if (selected) _selectedIds.Add(id);
        else _selectedIds.Remove(id);
    }

    private async Task DeleteSelectedAsync()
    {
        if (_selectedIds.Count == 0) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        List<DeploymentMirrorServer> toDelete = await db.Set<DeploymentMirrorServer>().Where(s => _selectedIds.Contains(s.Id)).ToListAsync();
        db.Set<DeploymentMirrorServer>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private async Task CreateMirrorServerAsync()
    {
        if (string.IsNullOrWhiteSpace(_newMirrorServer.Name) || string.IsNullOrWhiteSpace(_newMirrorServer.Url))
        {
            return;
        }

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        db.Set<DeploymentMirrorServer>().Add(_newMirrorServer);
        await db.SaveChangesAsync();

        _newMirrorServer = new DeploymentMirrorServer { Name = string.Empty, Url = string.Empty };
        await JS.InvokeVoidAsync("glpiNg.hideModal", "newMirrorServerModal");
        await LoadAsync();
    }
}
