using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Prism.Application.Admin;
using Prism.Application.SavedViews;
using Prism.Infrastructure.Persistence;
using Prism.Infrastructure.Persistence.Entities;

namespace Prism.Infrastructure.Admin;

/// <summary>Helpers shared by the admin stores: layout JSON, the audit record, and unique-violation detection.</summary>
internal static class AdminStoreSupport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Written from the validated object, so only the known properties are ever stored.</summary>
    public static string Serialize(SavedViewLayout layout) => JsonSerializer.Serialize(layout, Json);

    public static SavedViewLayout ReadLayout(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SavedViewLayout>(json, Json) ?? Empty();
        }
        catch (JsonException)
        {
            return Empty();
        }
    }

    public static int WidgetCount(string layoutJson) => ReadLayout(layoutJson).Widgets?.Count ?? 0;

    public static void AddAudit(PrismDbContext db, AdminActor actor, AdminAuditWrite write) =>
        db.AdminAuditEntries.Add(new AdminAuditEntry
        {
            ActorPortalUserId = actor.PortalUserId,
            Action = write.Action,
            ObjectType = write.ObjectType,
            ObjectId = write.ObjectId,
            ProviderId = write.ProviderId,
            AffectedCount = write.AffectedCount,
            TargetPortalUserId = write.TargetPortalUserId,
            Detail = write.Detail,
        });

    public static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static SavedViewLayout Empty() => new(SavedViewLimits.CurrentLayoutVersion, "comfortable", []);
}
