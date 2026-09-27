using GlpiNg.Modules.Deployment.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using GlpiNg.Modules.Abstractions.Localization;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class CollectDetail : ComponentBase, IAsyncDisposable
{
    [Parameter]
    public int CollectId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    private DbContext? _db;
    private CollectDefinition? _collect;
    private int _loadedId;
    private bool _isSaving;
    private string _activeTabKey = "main";

    private CollectRegistryEntry _newRegistryEntry = new() { Name = string.Empty, Hive = "HKEY_LOCAL_MACHINE", Path = string.Empty, RegistryKey = string.Empty };
    private CollectWmiEntry _newWmiEntry = new() { Name = string.Empty, WmiClass = string.Empty };
    private CollectFileSearchEntry _newFileSearchEntry = new() { Name = string.Empty, Path = string.Empty };

    protected override async Task OnParametersSetAsync()
    {
        if (_collect is not null && _loadedId == CollectId)
        {
            return;
        }

        _loadedId = CollectId;
        _activeTabKey = "main";

        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        _db = await DbFactory.CreateDbContextAsync();
        _collect = await _db.Set<CollectDefinition>()
            .Include(c => c.RegistryEntries)
            .Include(c => c.WmiEntries)
            .Include(c => c.FileSearchEntries)
            .FirstOrDefaultAsync(c => c.Id == CollectId);
    }

    private void SetTab(string key) => _activeTabKey = key;

    private async Task SaveAsync()
    {
        if (_db is null || _collect is null) return;

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
        if (_db is null || _collect is null) return;

        _db.Set<CollectDefinition>().Remove(_collect);
        await _db.SaveChangesAsync();
        Nav.NavigateTo("/tools/deployments/collects");
    }

    private async Task AddRegistryEntryAsync()
    {
        if (_db is null || _collect is null || string.IsNullOrWhiteSpace(_newRegistryEntry.Name)) return;

        _newRegistryEntry.CollectDefinitionId = _collect.Id;
        _collect.RegistryEntries.Add(_newRegistryEntry);
        await _db.SaveChangesAsync();
        _newRegistryEntry = new CollectRegistryEntry { Name = string.Empty, Hive = "HKEY_LOCAL_MACHINE", Path = string.Empty, RegistryKey = string.Empty };
    }

    private async Task RemoveRegistryEntryAsync(CollectRegistryEntry entry)
    {
        if (_db is null || _collect is null) return;

        _collect.RegistryEntries.Remove(entry);
        _db.Set<CollectRegistryEntry>().Remove(entry);
        await _db.SaveChangesAsync();
    }

    private async Task AddWmiEntryAsync()
    {
        if (_db is null || _collect is null || string.IsNullOrWhiteSpace(_newWmiEntry.Name)) return;

        _newWmiEntry.CollectDefinitionId = _collect.Id;
        _collect.WmiEntries.Add(_newWmiEntry);
        await _db.SaveChangesAsync();
        _newWmiEntry = new CollectWmiEntry { Name = string.Empty, WmiClass = string.Empty };
    }

    private async Task RemoveWmiEntryAsync(CollectWmiEntry entry)
    {
        if (_db is null || _collect is null) return;

        _collect.WmiEntries.Remove(entry);
        _db.Set<CollectWmiEntry>().Remove(entry);
        await _db.SaveChangesAsync();
    }

    private async Task AddFileSearchEntryAsync()
    {
        if (_db is null || _collect is null || string.IsNullOrWhiteSpace(_newFileSearchEntry.Name)) return;

        _newFileSearchEntry.CollectDefinitionId = _collect.Id;
        _collect.FileSearchEntries.Add(_newFileSearchEntry);
        await _db.SaveChangesAsync();
        _newFileSearchEntry = new CollectFileSearchEntry { Name = string.Empty, Path = string.Empty };
    }

    private async Task RemoveFileSearchEntryAsync(CollectFileSearchEntry entry)
    {
        if (_db is null || _collect is null) return;

        _collect.FileSearchEntries.Remove(entry);
        _db.Set<CollectFileSearchEntry>().Remove(entry);
        await _db.SaveChangesAsync();
    }

    private static string TypeLabel(CollectType type) => type switch
    {
        CollectType.Registry => Tr.T("Base de registre"),
        CollectType.Wmi => Tr.T("WMI"),
        CollectType.FileSearch => Tr.T("Recherche de fichier"),
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
