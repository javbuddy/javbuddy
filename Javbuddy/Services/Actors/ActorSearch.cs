using System.Linq.Expressions;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Actors;

/// <summary>The one predicate behind actor search and merge-candidate search.</summary>
public static class ActorSearch
{
    /// <summary>
    /// Actors whose name, either full-name order ("Hatano Yui" or "Yui Hatano"), Japanese name, R18.dev name or
    /// an alias contains <paramref name="term"/>. Case is ignored for ASCII only, as SQLite does.
    /// </summary>
    public static Expression<Func<Actor, bool>> Matches(string term)
    {
        var pattern = SqlLike.Contains(term.Trim());
        const string esc = SqlLike.EscapeChar;
        return a =>
            (a.FirstName != null && EF.Functions.Like(a.FirstName, pattern, esc))
            || (a.LastName != null && EF.Functions.Like(a.LastName, pattern, esc))
            || (a.FirstName != null && a.LastName != null
                && (EF.Functions.Like(a.LastName + " " + a.FirstName, pattern, esc)
                    || EF.Functions.Like(a.FirstName + " " + a.LastName, pattern, esc)))
            || (a.JapaneseNameKanji != null && EF.Functions.Like(a.JapaneseNameKanji, pattern, esc))
            || (a.JapaneseNameKana != null && EF.Functions.Like(a.JapaneseNameKana, pattern, esc))
            || (a.R18DevName != null && EF.Functions.Like(a.R18DevName, pattern, esc))
            || a.Aliases.Any(al => EF.Functions.Like(al.Name, pattern, esc));
    }
}
