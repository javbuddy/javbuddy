using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Javbuddy.Tests.TestSupport;

/// <summary>
/// Makes EF Core's async database calls against Postgres complete synchronously, the way Microsoft.Data.Sqlite's do.
/// Many component tests render a page and assert on the markup straight away; that only works because every awaited
/// SQLite call has already finished by the time the render returns. Real Postgres I/O completes later, so without this
/// those tests would race the page's loading. Test-only: the app itself always uses the real async path.
/// </summary>
internal sealed class SynchronousCompletionInterceptor : IDbConnectionInterceptor, IDbCommandInterceptor, IDbTransactionInterceptor
{
    public ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        connection.Open();
        return new ValueTask<InterceptionResult>(InterceptionResult.Suppress());
    }

    public ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        connection.Close();
        return new ValueTask<InterceptionResult>(InterceptionResult.Suppress());
    }

    public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
        new(InterceptionResult<DbDataReader>.SuppressWithResult(Execute(cancellationToken, command.ExecuteReader)));

    public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
        new(InterceptionResult<int>.SuppressWithResult(Execute(cancellationToken, command.ExecuteNonQuery)));

    public ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) =>
        new(InterceptionResult<object>.SuppressWithResult(Execute(cancellationToken, command.ExecuteScalar)!));

    public ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default) =>
        new(InterceptionResult<DbTransaction>.SuppressWithResult(Execute(cancellationToken, () => connection.BeginTransaction(eventData.IsolationLevel))));

    public ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
        return new ValueTask<InterceptionResult>(InterceptionResult.Suppress());
    }

    public ValueTask<InterceptionResult> TransactionRollingBackAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Rollback();
        return new ValueTask<InterceptionResult>(InterceptionResult.Suppress());
    }

    // A cancelled token must still fail the call, as the real async path would.
    private static T Execute<T>(CancellationToken cancellationToken, Func<T> call)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return call();
    }
}
