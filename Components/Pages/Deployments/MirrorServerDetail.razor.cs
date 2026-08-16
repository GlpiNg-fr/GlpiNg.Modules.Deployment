using GlpiNg.Modules.Deployment.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class MirrorServerDetail : ComponentBase, IAsyncDisposable
{
    [Parameter]
    public int MirrorServerId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    private DbContext? _db;
    private DeploymentMirrorServer? _mirrorServer;
    private int _loadedId;
    private bool _isSaving;

    protected override async Task OnParametersSetAsync()
    {
        if (_mirrorServer is not null && _loadedId == MirrorServerId)
        {
            return;
        }

        _loadedId = MirrorServerId;

        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        _db = await DbFactory.CreateDbContextAsync();
        _mirrorServer = await _db.Set<DeploymentMirrorServer>().FirstOrDefaultAsync(s => s.Id == MirrorServerId);
    }

    private async Task SaveAsync()
    {
        if (_db is null || _mirrorServer is null) return;

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
        if (_db is null || _mirrorServer is null) return;

        _db.Set<DeploymentMirrorServer>().Remove(_mirrorServer);
        await _db.SaveChangesAsync();
        Nav.NavigateTo("/tools/deployments/mirrors");
    }

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }
}
