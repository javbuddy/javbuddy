using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Data;

/// <summary>Tells a duplicate-key failure from the other SQLITE_CONSTRAINT errors (foreign key, NOT NULL, CHECK), which
/// all share primary error code 19 and so can't be told apart by it.</summary>
public static class SqliteConstraintErrors
{
    private const int ConstraintPrimaryKey = 1555; // SQLITE_CONSTRAINT_PRIMARYKEY: a duplicate composite key, e.g. MovieTag (MovieId, TagId)
    private const int ConstraintUnique = 2067;     // SQLITE_CONSTRAINT_UNIQUE: a duplicate in a unique index, e.g. Actor (FirstName, LastName)

    /// <summary>True when the save failed because a row with the same primary key or unique index value already exists.</summary>
    public static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteExtendedErrorCode: ConstraintPrimaryKey or ConstraintUnique };
}
