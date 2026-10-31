using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Grid.Api.Data;

public static class DbErrors
{
    /// <summary>True when the save failed on the named unique index (or any unique index if null).</summary>
    public static bool IsUniqueViolation(this DbUpdateException ex, string? indexName = null) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && (indexName is null || pg.ConstraintName == indexName);
}
