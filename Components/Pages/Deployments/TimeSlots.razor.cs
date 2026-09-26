using GlpiNg.Modules.Deployment.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class TimeSlots : ComponentBase
{
    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private List<TimeSlot> _timeSlots = [];
    private readonly HashSet<int> _selectedIds = [];
    private TimeSlot _newTimeSlot = new() { Name = string.Empty };

    private bool AllSelected => _timeSlots.Count > 0 && _selectedIds.Count == _timeSlots.Count;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _timeSlots = await db.Set<TimeSlot>().AsNoTracking().OrderBy(t => t.Name).ToListAsync();
        _selectedIds.Clear();
    }

    private void ToggleSelectAll(bool selectAll)
    {
        _selectedIds.Clear();
        if (selectAll)
        {
            foreach (TimeSlot timeSlot in _timeSlots)
            {
                _selectedIds.Add(timeSlot.Id);
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
        List<TimeSlot> toDelete = await db.Set<TimeSlot>().Where(t => _selectedIds.Contains(t.Id)).ToListAsync();
        db.Set<TimeSlot>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private async Task CreateTimeSlotAsync()
    {
        if (string.IsNullOrWhiteSpace(_newTimeSlot.Name))
        {
            return;
        }

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        db.Set<TimeSlot>().Add(_newTimeSlot);
        await db.SaveChangesAsync();

        _newTimeSlot = new TimeSlot { Name = string.Empty };
        await JS.InvokeVoidAsync("glping.hideModal", "newTimeSlotModal");
        await LoadAsync();
    }
}
