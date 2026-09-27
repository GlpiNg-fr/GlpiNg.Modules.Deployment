using GlpiNg.Modules.Deployment.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using GlpiNg.Modules.Abstractions.Localization;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class UserInteractionTemplates : ComponentBase
{
    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private List<DeploymentUserInteractionTemplate> _templates = [];
    private readonly HashSet<int> _selectedIds = [];
    private DeploymentUserInteractionTemplate _newTemplate = new() { Name = string.Empty };

    private bool AllSelected => _templates.Count > 0 && _selectedIds.Count == _templates.Count;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _templates = await db.Set<DeploymentUserInteractionTemplate>().AsNoTracking().OrderBy(t => t.Name).ToListAsync();
        _selectedIds.Clear();
    }

    private void ToggleSelectAll(bool selectAll)
    {
        _selectedIds.Clear();
        if (selectAll)
        {
            foreach (DeploymentUserInteractionTemplate template in _templates)
            {
                _selectedIds.Add(template.Id);
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
        List<DeploymentUserInteractionTemplate> toDelete = await db.Set<DeploymentUserInteractionTemplate>().Where(t => _selectedIds.Contains(t.Id)).ToListAsync();
        db.Set<DeploymentUserInteractionTemplate>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private async Task CreateTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(_newTemplate.Name) || string.IsNullOrWhiteSpace(_newTemplate.Text))
        {
            return;
        }

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        db.Set<DeploymentUserInteractionTemplate>().Add(_newTemplate);
        await db.SaveChangesAsync();

        _newTemplate = new DeploymentUserInteractionTemplate { Name = string.Empty };
        await JS.InvokeVoidAsync("glping.hideModal", "newTemplateModal");
        await LoadAsync();
    }

    private static string InteractionTypeLabel(DeploymentUserInteractionType type) => type switch
    {
        DeploymentUserInteractionType.InfoMessage => Tr.T("Message d'information"),
        DeploymentUserInteractionType.AcceptRefuse => Tr.T("Accepter / Refuser"),
        _ => type.ToString()
    };
}
