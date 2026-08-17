using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Deployment.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class Package : ComponentBase
{
    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    [Inject]
    private DeploymentPackageFileStorageService FileStorage { get; set; } = null!;

    private List<DeploymentPackage> _packages = [];
    private List<DeploymentPackage> _filteredPackages = [];
    private string _searchTerm = string.Empty;
    private readonly HashSet<int> _selectedIds = [];
    private DeploymentPackage _newPackage = NewBlankPackage();

    private bool AllSelected => _filteredPackages.Count > 0 && _selectedIds.Count == _filteredPackages.Count;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _packages = await db.Set<DeploymentPackage>()
            .AsNoTracking()
            .Include(package => package.Files)
            .Include(package => package.DeployComputerGroup)
            .Include(package => package.SupersededByPackage)
            .OrderBy(package => package.Name)
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

        IEnumerable<DeploymentPackage> query = term.Length == 0
            ? _packages
            : _packages.Where(package => MatchesSearch(package, term));

        _filteredPackages = query.OrderBy(package => package.Name).ToList();
        _selectedIds.IntersectWith(_filteredPackages.Select(package => package.Id));
    }

    private void ToggleSelectAll(bool selectAll)
    {
        _selectedIds.Clear();
        if (selectAll)
        {
            foreach (DeploymentPackage package in _filteredPackages)
            {
                _selectedIds.Add(package.Id);
            }
        }
    }

    private void ToggleSelect(int packageId, bool selected)
    {
        if (selected)
        {
            _selectedIds.Add(packageId);
        }
        else
        {
            _selectedIds.Remove(packageId);
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (_selectedIds.Count == 0) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        List<DeploymentPackage> toDelete = await db.Set<DeploymentPackage>()
            .Include(package => package.Files).ThenInclude(file => file.Parts)
            .Where(package => _selectedIds.Contains(package.Id))
            .ToListAsync();

        List<string> storagePaths = toDelete.SelectMany(package => package.Files)
            .SelectMany(file => file.Parts)
            .Select(part => part.StoragePath)
            .ToList();

        db.Set<DeploymentPackage>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await FileStorage.DeleteOrphanedPartsAsync(db, storagePaths);

        await LoadAsync();
    }

    private async Task CreatePackageAsync()
    {
        if (string.IsNullOrWhiteSpace(_newPackage.Name))
        {
            return;
        }

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        db.Set<DeploymentPackage>().Add(_newPackage);
        await db.SaveChangesAsync();

        _newPackage = NewBlankPackage();
        await JS.InvokeVoidAsync("glpiNg.hideModal", "newPackageModal");
        await LoadAsync();
    }

    private static DeploymentPackage NewBlankPackage() => new() { Name = string.Empty };

    private static int ActionCount(DeploymentPackage package) =>
        DeploymentPackageJsonConverter.ParseJsonArray(package.ActionsJson).Count;

    private static bool MatchesSearch(DeploymentPackage package, string term)
    {
        return package.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || (package.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
