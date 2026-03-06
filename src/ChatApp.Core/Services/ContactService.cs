using ChatApp.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Core.Services;

/// <inheritdoc cref="IContactService"/>
public sealed class ContactService : IContactService
{
    private readonly LocalDb _db;

    public ContactService(LocalDb db) => _db = db;

    public async Task UpsertAsync(ContactEntry contact)
    {
        var existing = await _db.Contacts.FindAsync(contact.UserName);
        if (existing is null)
        {
            _db.Contacts.Add(new ContactRow
            {
                UserName    = contact.UserName,
                DisplayName = contact.DisplayName,
                Status      = (int)contact.Status,
                IsIncoming  = contact.IsIncoming,
                CreatedAt   = contact.CreatedAt
            });
        }
        else
        {
            existing.DisplayName = contact.DisplayName;
            existing.Status      = (int)contact.Status;
            existing.IsIncoming  = contact.IsIncoming;
        }
        await _db.SaveChangesAsync();
    }

    public async Task<List<ContactEntry>> GetByStatusAsync(params ContactStatus[] statuses)
    {
        var codes = statuses.Select(s => (int)s).ToList();
        var rows  = await _db.Contacts
            .Where(c => codes.Contains(c.Status))
            .OrderBy(c => c.DisplayName)
            .ToListAsync();

        return rows.Select(Map).ToList();
    }

    public async Task<ContactEntry?> GetAsync(string userName)
    {
        var row = await _db.Contacts.FindAsync(userName);
        return row is null ? null : Map(row);
    }

    public async Task<bool> IsBlockedAsync(string userName)
    {
        var row = await _db.Contacts.FindAsync(userName);
        return row?.Status == (int)ContactStatus.Blocked;
    }

    public async Task RemoveAsync(string userName)
    {
        var row = await _db.Contacts.FindAsync(userName);
        if (row is not null)
        {
            _db.Contacts.Remove(row);
            await _db.SaveChangesAsync();
        }
    }

    private static ContactEntry Map(ContactRow r) => new(
        r.UserName,
        r.DisplayName,
        (ContactStatus)r.Status,
        r.IsIncoming,
        r.CreatedAt);
}
