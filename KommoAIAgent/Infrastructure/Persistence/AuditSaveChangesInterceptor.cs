using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace KommoAIAgent.Infrastructure.Persistence;

/// <summary>
/// Auditoría automática de CreatedAt y UpdatedAt en las entidades.
/// </summary>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        var ctx = eventData.Context;
        if (ctx is null) return base.SavingChanges(eventData, result);

        var now = DateTime.UtcNow;

        var entries = ctx.ChangeTracker.Entries()
            .Where(e => e.Entity is { } && (e.State == EntityState.Added || e.State == EntityState.Modified));

        foreach (var e in entries)
        {
            var entity = e.Entity;
            var createdAtProp = e.Properties.FirstOrDefault(p => p.Metadata.Name == "CreatedAt");
            var updatedAtProp = e.Properties.FirstOrDefault(p => p.Metadata.Name == "UpdatedAt");

            if (e.State == EntityState.Added && createdAtProp is not null)
                createdAtProp.CurrentValue = createdAtProp.CurrentValue ?? now;

            if (updatedAtProp is not null)
                updatedAtProp.CurrentValue = now;
        }

        return base.SavingChanges(eventData, result);
    }
}
