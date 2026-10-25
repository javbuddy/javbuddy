using Javbuddy.Data;
using Javbuddy.Services.Scenes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Javbuddy.Tests.TestSupport;

/// <summary>
/// An <see cref="IDbContextFactory{TContext}"/> backed by an in-memory SQLite database (or, when
/// <c>JAVBUDDY_TEST_POSTGRES</c> is set, a throwaway Postgres database cloned from a migrated template: see
/// <see cref="PostgresTestDatabase"/>), so
/// tests exercise the same provider (constraints, indexes, conversions) as production instead
/// of EF Core's InMemory provider, which silently ignores relational behavior.
/// The underlying connection must stay open for the lifetime of the in-memory database, so
/// dispose the factory itself once the test is done with it.
/// By default every context shares that one connection, which isn't thread-safe: a test whose
/// contexts run concurrently uses <see cref="WithConnectionPerContext"/> instead.
/// </summary>
public sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly SqliteConnection? _connection;
    private readonly Func<AppDbContext> _create;
    private readonly string? _postgresDatabase;

    /// <summary>What the contexts' ClipActorStaleInterceptor signals, as the app's worker would see it.</summary>
    public ClipActorRefreshSignal ClipActorSignal { get; } = new();

    public TestDbContextFactory(params IInterceptor[] interceptors)
        : this(connectionPerContext: false, interceptors)
    {
    }

    private TestDbContextFactory(bool connectionPerContext, IInterceptor[] interceptors)
    {
        // As in the app, every save keeps the stored effective actors' stale flag in step.
        IInterceptor[] allInterceptors = [new ClipActorStaleInterceptor(ClipActorSignal), .. interceptors];

        if (PostgresTestDatabase.IsEnabled)
        {
            // Postgres gives every context its own connection, so connectionPerContext changes nothing.
            var (connectionString, name) = PostgresTestDatabase.CreateDatabase();
            _postgresDatabase = name;
            var postgresOptions = PostgresTestDatabase.Options(connectionString)
                .AddInterceptors([new SynchronousCompletionInterceptor(), .. allInterceptors])
                .Options;
            _create = () => new PostgresAppDbContext(postgresOptions);
            return;
        }

        // A named shared-cache database lets each context open its own connection to it, as
        // production's file database does; the factory's connection keeps it alive. Pooling is off
        // so no pooled connection keeps it alive after the factory is disposed.
        var sqliteConnectionString = connectionPerContext
            ? $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared;Pooling=False"
            : "DataSource=:memory:";
        _connection = new SqliteConnection(sqliteConnectionString);
        _connection.Open();

        var builder = new DbContextOptionsBuilder<AppDbContext>();
        Action<SqliteDbContextOptionsBuilder> sqliteOptions = o =>
            o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        var options = (connectionPerContext
                ? builder.UseSqlite(sqliteConnectionString, sqliteOptions)
                : builder.UseSqlite(_connection, sqliteOptions))
            .AddInterceptors(allInterceptors)
            .Options;
        _create = () => new AppDbContext(options);

        using var context = _create();
        context.Database.EnsureCreated();
    }

    /// <summary>A factory whose contexts each open their own connection, for tests that use
    /// several contexts at once.</summary>
    public static TestDbContextFactory WithConnectionPerContext(params IInterceptor[] interceptors) =>
        new(connectionPerContext: true, interceptors);

    public AppDbContext CreateDbContext() => _create();

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());

    public void Dispose()
    {
        _connection?.Dispose();
        if (_postgresDatabase is not null)
        {
            PostgresTestDatabase.DropDatabase(_postgresDatabase);
        }
    }
}
