using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TicketHub.BuildingBlocks.Persistence;

public static class SqliteCompat
{
    /// <summary>
    /// Fast tests run on SQLite, which can't compare or sort DateTimeOffset and has no rowversion.
    /// No-op on SQL Server.
    /// </summary>
    public static ModelBuilder ForSqliteTests(this ModelBuilder model, DatabaseFacade database)
    {
        if (database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite") return model;

        foreach (var entity in model.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                    property.SetValueConverter(new DateTimeOffsetToBinaryConverter());

                if (property.ClrType == typeof(byte[]) && property.IsConcurrencyToken)
                {
                    property.IsConcurrencyToken = false;
                    property.ValueGenerated = ValueGenerated.Never;
                }
            }
        }
        return model;
    }
}
