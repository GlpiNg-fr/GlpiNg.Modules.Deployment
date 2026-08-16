using GlpiNg.Modules.Deployment.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class TimeSlotDetail : ComponentBase, IAsyncDisposable
{
    [Parameter]
    public int TimeSlotId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    private DbContext? _db;
    private TimeSlot? _timeSlot;
    private int _loadedId;
    private bool _isSaving;

    private DayOfWeek _newDay = DayOfWeek.Monday;
    private TimeOnly _newStart = new(9, 0);
    private TimeOnly _newEnd = new(17, 0);

    protected override async Task OnParametersSetAsync()
    {
        if (_timeSlot is not null && _loadedId == TimeSlotId)
        {
            return;
        }

        _loadedId = TimeSlotId;

        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        _db = await DbFactory.CreateDbContextAsync();
        _timeSlot = await _db.Set<TimeSlot>()
            .Include(t => t.Entries)
            .FirstOrDefaultAsync(t => t.Id == TimeSlotId);
    }

    private async Task SaveAsync()
    {
        if (_db is null || _timeSlot is null) return;

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
        if (_db is null || _timeSlot is null) return;

        _db.Set<TimeSlot>().Remove(_timeSlot);
        await _db.SaveChangesAsync();
        Nav.NavigateTo("/tools/deployments/timeslots");
    }

    private async Task AddEntryAsync()
    {
        if (_db is null || _timeSlot is null) return;

        _timeSlot.Entries.Add(new TimeSlotEntry
        {
            TimeSlotId = _timeSlot.Id,
            DayOfWeek = _newDay,
            StartTime = _newStart,
            EndTime = _newEnd
        });

        await _db.SaveChangesAsync();
    }

    private async Task RemoveEntryAsync(TimeSlotEntry entry)
    {
        if (_db is null || _timeSlot is null) return;

        _timeSlot.Entries.Remove(entry);
        _db.Set<TimeSlotEntry>().Remove(entry);
        await _db.SaveChangesAsync();
    }

    private static string DayLabel(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Lundi",
        DayOfWeek.Tuesday => "Mardi",
        DayOfWeek.Wednesday => "Mercredi",
        DayOfWeek.Thursday => "Jeudi",
        DayOfWeek.Friday => "Vendredi",
        DayOfWeek.Saturday => "Samedi",
        DayOfWeek.Sunday => "Dimanche",
        _ => day.ToString()
    };

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }
}
