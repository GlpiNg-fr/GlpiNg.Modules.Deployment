using GlpiNg.Modules.Deployment.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class SnmpCredentials : ComponentBase
{
    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private List<SnmpCredential> _credentials = [];
    private readonly HashSet<int> _selectedIds = [];
    private int? _editingId;
    private SnmpCredentialForm _editForm = new();

    private bool AllSelected => _credentials.Count > 0 && _selectedIds.Count == _credentials.Count;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _credentials = await db.Set<SnmpCredential>().AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        _selectedIds.Clear();
    }

    private static string VersionLabel(SnmpVersion version) => version switch
    {
        SnmpVersion.V1 => "1",
        SnmpVersion.V2c => "2c",
        SnmpVersion.V3 => "3",
        _ => version.ToString()
    };

    private void ToggleSelectAll(bool selectAll)
    {
        _selectedIds.Clear();
        if (selectAll)
        {
            foreach (SnmpCredential credential in _credentials)
            {
                _selectedIds.Add(credential.Id);
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
        List<SnmpCredential> toDelete = await db.Set<SnmpCredential>().Where(c => _selectedIds.Contains(c.Id)).ToListAsync();
        db.Set<SnmpCredential>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private async Task OpenCreateModalAsync()
    {
        _editingId = null;
        _editForm = new SnmpCredentialForm();
        await JS.InvokeVoidAsync("glpiNg.showModal", "snmpCredentialModal");
    }

    private async Task OpenEditModalAsync(SnmpCredential credential)
    {
        _editingId = credential.Id;
        _editForm = new SnmpCredentialForm
        {
            Name = credential.Name,
            Version = credential.Version,
            Community = credential.Community,
            Username = credential.Username,
            AuthProtocol = credential.AuthProtocol,
            AuthPassphrase = credential.AuthPassphrase,
            PrivProtocol = credential.PrivProtocol,
            PrivPassphrase = credential.PrivPassphrase
        };
        await JS.InvokeVoidAsync("glpiNg.showModal", "snmpCredentialModal");
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_editForm.Name))
        {
            return;
        }

        await using DbContext db = await DbFactory.CreateDbContextAsync();

        if (_editingId is int id)
        {
            SnmpCredential? credential = await db.Set<SnmpCredential>().FirstOrDefaultAsync(c => c.Id == id);
            if (credential is null) return;

            ApplyForm(credential);
        }
        else
        {
            SnmpCredential credential = new() { Name = _editForm.Name };
            ApplyForm(credential);
            db.Set<SnmpCredential>().Add(credential);
        }

        await db.SaveChangesAsync();

        await JS.InvokeVoidAsync("glpiNg.hideModal", "snmpCredentialModal");
        await LoadAsync();
    }

    private void ApplyForm(SnmpCredential credential)
    {
        credential.Name = _editForm.Name;
        credential.Version = _editForm.Version;

        if (_editForm.Version is SnmpVersion.V1 or SnmpVersion.V2c)
        {
            credential.Community = _editForm.Community;
            credential.Username = null;
            credential.AuthProtocol = SnmpAuthProtocol.None;
            credential.AuthPassphrase = null;
            credential.PrivProtocol = SnmpPrivProtocol.None;
            credential.PrivPassphrase = null;
        }
        else
        {
            credential.Community = null;
            credential.Username = _editForm.Username;
            credential.AuthProtocol = _editForm.AuthProtocol;
            credential.AuthPassphrase = _editForm.AuthPassphrase;
            credential.PrivProtocol = _editForm.PrivProtocol;
            credential.PrivPassphrase = _editForm.PrivPassphrase;
        }
    }

    private class SnmpCredentialForm
    {
        public string Name { get; set; } = string.Empty;
        public SnmpVersion Version { get; set; } = SnmpVersion.V2c;
        public string? Community { get; set; }
        public string? Username { get; set; }
        public SnmpAuthProtocol AuthProtocol { get; set; } = SnmpAuthProtocol.None;
        public string? AuthPassphrase { get; set; }
        public SnmpPrivProtocol PrivProtocol { get; set; } = SnmpPrivProtocol.None;
        public string? PrivPassphrase { get; set; }
    }
}
