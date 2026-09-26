using GlpiNg.Modules.Deployment.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class IpRanges : ComponentBase
{
    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private List<IpRange> _ranges = [];
    private readonly HashSet<int> _selectedIds = [];
    private int? _editingId;
    private IpRangeForm _editForm = new();

    private bool AllSelected => _ranges.Count > 0 && _selectedIds.Count == _ranges.Count;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _ranges = await db.Set<IpRange>().AsNoTracking().OrderBy(r => r.Name).ToListAsync();
        _selectedIds.Clear();
    }

    private void ToggleSelectAll(bool selectAll)
    {
        _selectedIds.Clear();
        if (selectAll)
        {
            foreach (IpRange range in _ranges)
            {
                _selectedIds.Add(range.Id);
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
        List<IpRange> toDelete = await db.Set<IpRange>().Where(r => _selectedIds.Contains(r.Id)).ToListAsync();
        db.Set<IpRange>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private async Task OpenCreateModalAsync()
    {
        _editingId = null;
        _editForm = new IpRangeForm();
        await JS.InvokeVoidAsync("glping.showModal", "ipRangeModal");
    }

    private async Task OpenEditModalAsync(IpRange range)
    {
        _editingId = range.Id;
        _editForm = new IpRangeForm { Name = range.Name, StartIp = range.StartIp, EndIp = range.EndIp, Comment = range.Comment };
        await JS.InvokeVoidAsync("glping.showModal", "ipRangeModal");
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_editForm.Name) || string.IsNullOrWhiteSpace(_editForm.StartIp) || string.IsNullOrWhiteSpace(_editForm.EndIp))
        {
            return;
        }

        await using DbContext db = await DbFactory.CreateDbContextAsync();

        if (_editingId is int id)
        {
            IpRange? range = await db.Set<IpRange>().FirstOrDefaultAsync(r => r.Id == id);
            if (range is null) return;

            range.Name = _editForm.Name;
            range.StartIp = _editForm.StartIp;
            range.EndIp = _editForm.EndIp;
            range.Comment = _editForm.Comment;
        }
        else
        {
            db.Set<IpRange>().Add(new IpRange
            {
                Name = _editForm.Name,
                StartIp = _editForm.StartIp,
                EndIp = _editForm.EndIp,
                Comment = _editForm.Comment
            });
        }

        await db.SaveChangesAsync();

        await JS.InvokeVoidAsync("glping.hideModal", "ipRangeModal");
        await LoadAsync();
    }

    private class IpRangeForm
    {
        public string Name { get; set; } = string.Empty;
        public string StartIp { get; set; } = string.Empty;
        public string EndIp { get; set; } = string.Empty;
        public string? Comment { get; set; }
    }
}
