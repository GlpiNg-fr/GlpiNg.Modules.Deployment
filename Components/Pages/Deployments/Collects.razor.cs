using GlpiNg.Modules.Deployment.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using GlpiNg.Modules.Abstractions.Localization;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class Collects : ComponentBase
{
    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private List<CollectDefinition> _collects = [];
    private readonly HashSet<int> _selectedIds = [];
    private CollectDefinition _newCollect = new() { Name = string.Empty };

    private bool AllSelected => _collects.Count > 0 && _selectedIds.Count == _collects.Count;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _collects = await db.Set<CollectDefinition>().AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        _selectedIds.Clear();
    }

    private void ToggleSelectAll(bool selectAll)
    {
        _selectedIds.Clear();
        if (selectAll)
        {
            foreach (CollectDefinition collect in _collects)
            {
                _selectedIds.Add(collect.Id);
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
        List<CollectDefinition> toDelete = await db.Set<CollectDefinition>().Where(c => _selectedIds.Contains(c.Id)).ToListAsync();
        db.Set<CollectDefinition>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private async Task CreateCollectAsync()
    {
        if (string.IsNullOrWhiteSpace(_newCollect.Name))
        {
            return;
        }

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        db.Set<CollectDefinition>().Add(_newCollect);
        await db.SaveChangesAsync();

        _newCollect = new CollectDefinition { Name = string.Empty };
        await JS.InvokeVoidAsync("glping.hideModal", "newCollectModal");
        await LoadAsync();
    }

    private static string TypeLabel(CollectType type) => type switch
    {
        CollectType.Registry => Tr.T("Base de registre"),
        CollectType.Wmi => Tr.T("WMI"),
        CollectType.FileSearch => Tr.T("Recherche de fichier"),
        _ => type.ToString()
    };
}
