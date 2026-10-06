using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Javbuddy.Tests.TestSupport;

/// <summary>
/// Counts SELECT commands EF Core sends to the database, so tests can assert a batch operation's
/// read count stays flat as its input grows instead of issuing one query per item. Writes from
/// SaveChanges are excluded — only reads are counted.
/// </summary>
public sealed class SelectCommandCounter : DbCommandInterceptor
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Reset() => Interlocked.Exchange(ref _count, 0);

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        CountIfSelect(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        CountIfSelect(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        CountIfSelect(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        CountIfSelect(command);
        return ValueTask.FromResult(result);
    }

    private void CountIfSelect(DbCommand command)
    {
        if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _count);
        }
    }
}
