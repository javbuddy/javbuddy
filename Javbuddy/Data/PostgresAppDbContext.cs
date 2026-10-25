using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Javbuddy.Data;

/// <summary>The Postgres flavour of <see cref="AppDbContext"/>: the same model with the provider differences applied inside
/// <see cref="AppDbContext"/>, and its own migration set under <c>Migrations/Postgres/</c>. Call sites keep using
/// <c>IDbContextFactory&lt;AppDbContext&gt;</c>; only Program.cs and the migrations know this type exists.</summary>
public sealed class PostgresAppDbContext(DbContextOptions<PostgresAppDbContext> options) : AppDbContext(options);

/// <summary>Lets <c>dotnet ef migrations add --context PostgresAppDbContext</c> run without a database server.
/// (EF treats a design-time factory of a derived context as a match for its base type too, so
/// <see cref="SqliteAppDbContextDesignTimeFactory"/> pins <c>--context AppDbContext</c> to SQLite.)</summary>
public sealed class PostgresAppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PostgresAppDbContext>
{
    public PostgresAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresAppDbContext>()
            .UseNpgsql("Host=localhost;Database=javbuddy_design_time")
            .Options);
}

/// <summary>Makes <c>dotnet ef migrations add --context AppDbContext</c> build the SQLite model even though a Postgres
/// design-time factory for the derived context exists.</summary>
public sealed class SqliteAppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=design-time.db").Options);
}

/// <summary>Exposes a <see cref="PostgresAppDbContext"/> factory as the <c>IDbContextFactory&lt;AppDbContext&gt;</c> the rest of the app injects.</summary>
public sealed class PostgresAppDbContextFactoryAdapter(IDbContextFactory<PostgresAppDbContext> inner) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => inner.CreateDbContext();

    public async Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        await inner.CreateDbContextAsync(cancellationToken);
}
