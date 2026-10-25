using System.Linq.Expressions;
using System.Reflection;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Data;

/// <summary>Case-insensitive substring and prefix predicates over text columns, in the shape each provider answers best.
/// SQLite: <c>EF.Functions.Like</c> (ASCII case-insensitive already, and a prefix pattern on the NOCASE <c>Movie.Code</c>
/// seeks its index). Postgres: <c>ILIKE</c>, which the <c>gin_trgm_ops</c> indexes (see <see cref="AppDbContext"/>) serve and
/// which, unlike <c>lower(column)</c>, keeps the column's statistics. <c>ILIKE</c> refuses a nondeterministic collation
/// (<c>Movie.Code</c>, <c>CachedImage.Code</c>, or any expression over them), so every searched expression is cast with
/// <c>COLLATE "default"</c>, which the <c>Movie.Code</c> trigram index is declared with too. Query code calls these instead of branching on the provider.</summary>
public static class CaseInsensitiveSearch
{
    private static readonly MethodInfo LikeMethod = typeof(DbFunctionsExtensions).GetMethod(
        nameof(DbFunctionsExtensions.Like), [typeof(DbFunctions), typeof(string), typeof(string)])!;

    private static readonly MethodInfo ILikeMethod = typeof(NpgsqlDbFunctionsExtensions).GetMethod(
        nameof(NpgsqlDbFunctionsExtensions.ILike), [typeof(DbFunctions), typeof(string), typeof(string)])!;

    private static readonly MethodInfo CollateMethod = typeof(RelationalDbFunctionsExtensions).GetMethod(
        nameof(RelationalDbFunctionsExtensions.Collate), 1, [typeof(DbFunctions), Type.MakeGenericMethodParameter(0), typeof(string)])!
        .MakeGenericMethod(typeof(string));

    /// <summary>The Postgres collation <c>ILIKE</c> needs on a nondeterministic-collation column, and the trigram index's.</summary>
    public const string PostgresIndexCollation = "default";

    /// <summary>A predicate that is true when any of <paramref name="columns"/> contains <paramref name="term"/>.</summary>
    public static Expression<Func<T, bool>> ContainsAny<T>(this AppDbContext db, string term, params Expression<Func<T, string?>>[] columns) =>
        Build(db, $"%{term}%", columns);

    /// <summary>A predicate that is true when any of <paramref name="columns"/> starts with <paramref name="prefix"/>.</summary>
    public static Expression<Func<T, bool>> StartsWithAny<T>(this AppDbContext db, string prefix, params Expression<Func<T, string?>>[] columns) =>
        Build(db, $"{prefix}%", columns);

    /// <summary>A predicate that is true when any of the row's own <paramref name="ownColumns"/>, or of the columns of its
    /// <paramref name="movie"/> named by <paramref name="movieColumns"/>, contains <paramref name="term"/>. SQLite keeps the
    /// one flat OR across the join. Postgres cannot use a trigram index for an OR that spans a join, so there it is the row's
    /// own match OR <c>MovieId IN (SELECT Id FROM Movies WHERE …)</c>, which lets the movie side use its indexes.
    /// Both shapes select the same rows.</summary>
    public static Expression<Func<T, bool>> ContainsAnyOrMovie<T>(this AppDbContext db, string term,
        Expression<Func<T, Movie>> movie, Expression<Func<T, int>> movieId,
        Expression<Func<Movie, string?>>[] movieColumns, params Expression<Func<T, string?>>[] ownColumns)
    {
        if (db.Database.IsNpgsql())
        {
            var movieIds = db.Movies.Where(ContainsAny<Movie>(db, term, movieColumns)).Select(m => m.Id);
            var inMovies = Expression.Call(typeof(Queryable), nameof(Queryable.Contains), [typeof(int)], movieIds.Expression, movieId.Body);
            var inMoviesPredicate = Expression.Lambda<Func<T, bool>>(inMovies, movieId.Parameters);
            return ownColumns.Length == 0 ? inMoviesPredicate : ContainsAny(db, term, ownColumns).Or(inMoviesPredicate);
        }

        var viaMovie = movieColumns.Select(column =>
            Expression.Lambda<Func<T, string?>>(
                new ParameterReplacer(column.Parameters[0], movie.Body).Visit(column.Body), movie.Parameters));
        return ContainsAny(db, term, [.. ownColumns, .. viaMovie]);
    }

    /// <summary>A predicate that is true when both are, as one <c>AND</c> over the shared parameter.</summary>
    public static Expression<Func<T, bool>> And<T>(this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right) =>
        Combine(left, right, Expression.AndAlso);

    /// <summary>A predicate that is true when either is, as one <c>OR</c> over the shared parameter.</summary>
    public static Expression<Func<T, bool>> Or<T>(this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right) =>
        Combine(left, right, Expression.OrElse);

    private static Expression<Func<T, bool>> Combine<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right,
        Func<Expression, Expression, BinaryExpression> join) =>
        Expression.Lambda<Func<T, bool>>(
            join(left.Body, new ParameterReplacer(right.Parameters[0], left.Parameters[0]).Visit(right.Body)),
            left.Parameters);

    private static Expression<Func<T, bool>> Build<T>(AppDbContext db, string pattern, Expression<Func<T, string?>>[] columns)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        // A member of a constant becomes a query parameter, not an inlined literal, so the query plan stays cacheable.
        var patternValue = Expression.Property(Expression.Constant(new PatternHolder(pattern)), nameof(PatternHolder.Value));
        var functions = Expression.Constant(EF.Functions);
        var postgres = db.Database.IsNpgsql();

        Expression? any = null;
        foreach (var column in columns)
        {
            var body = new ParameterReplacer(column.Parameters[0], parameter).Visit(column.Body);
            // ILIKE refuses a nondeterministic collation anywhere in its operand, whether on a column or inside an
            // expression over one, so every searched expression is cast. On a column that already has the default
            // collation the cast is a no-op and its trigram index still applies.
            var subject = postgres
                ? Expression.Call(CollateMethod, functions, body, Expression.Constant(PostgresIndexCollation))
                : body;

            var match = Expression.AndAlso(
                Expression.NotEqual(body, Expression.Constant(null, typeof(string))),
                Expression.Call(postgres ? ILikeMethod : LikeMethod, functions, subject, patternValue));
            any = any is null ? match : Expression.OrElse(any, match);
        }

        return Expression.Lambda<Func<T, bool>>(any ?? Expression.Constant(false), parameter);
    }

    private sealed record PatternHolder(string Value);

    private sealed class ParameterReplacer(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
