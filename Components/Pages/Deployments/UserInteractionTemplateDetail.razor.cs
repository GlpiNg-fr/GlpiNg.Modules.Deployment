using GlpiNg.Modules.Deployment.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class UserInteractionTemplateDetail : ComponentBase, IAsyncDisposable
{
    [Parameter]
    public int TemplateId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    private DbContext? _db;
    private DeploymentUserInteractionTemplate? _template;
    private int _loadedId;
    private bool _isSaving;

    protected override async Task OnParametersSetAsync()
    {
        if (_template is not null && _loadedId == TemplateId)
        {
            return;
        }

        _loadedId = TemplateId;

        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        _db = await DbFactory.CreateDbContextAsync();
        _template = await _db.Set<DeploymentUserInteractionTemplate>().FirstOrDefaultAsync(t => t.Id == TemplateId);
    }

    private async Task SaveAsync()
    {
        if (_db is null || _template is null) return;

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
        if (_db is null || _template is null) return;

        _db.Set<DeploymentUserInteractionTemplate>().Remove(_template);
        await _db.SaveChangesAsync();
        Nav.NavigateTo("/tools/deployments/interaction-templates");
    }

    private static string InteractionTypeLabel(DeploymentUserInteractionType type) => type switch
    {
        DeploymentUserInteractionType.InfoMessage => "Message d'information",
        DeploymentUserInteractionType.AcceptRefuse => "Accepter / Refuser",
        _ => type.ToString()
    };

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }
}
