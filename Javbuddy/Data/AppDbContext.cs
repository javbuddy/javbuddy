using System.Text.Json;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Javbuddy.Data;

/// <summary>The app's EF Core model. This type is the SQLite context (its migrations live in <c>Migrations/</c>);
/// <see cref="PostgresAppDbContext"/> derives from it for the opt-in Postgres provider, with its own migrations in
/// <c>Migrations/Postgres/</c>. The few provider differences are gated on <c>Database.IsNpgsql()</c> below.
/// Every schema change therefore needs a migration for both contexts.</summary>
public class AppDbContext : DbContext
{
    /// <summary>Case-insensitive ICU collation the Postgres context gives <c>Movie.Code</c>, matching SQLite's NOCASE.</summary>
    public const string PostgresCaseInsensitiveCollation = "case_insensitive";

    // Two constructors (so the Postgres subclass can pass its own options type), hence not a primary constructor.
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected AppDbContext(DbContextOptions options) : base(options)
    {
    }

    private bool IsPostgres => Database.IsNpgsql();

    /// <summary>The provider's case-insensitive collation, for <c>EF.Functions.Collate</c> (NOCASE on SQLite, an ICU collation on Postgres).</summary>
    public string CaseInsensitiveCollation => IsPostgres ? PostgresCaseInsensitiveCollation : "NOCASE";

    public DbSet<Movie> Movies => Set<Movie>();
    public DbSet<DeletedMovie> DeletedMovies => Set<DeletedMovie>();
    public DbSet<MovieFile> MovieFiles => Set<MovieFile>();
    public DbSet<Actor> Actors => Set<Actor>();
    public DbSet<ActorAlias> ActorAliases => Set<ActorAlias>();
    public DbSet<ActorCupSizePeriod> ActorCupSizePeriods => Set<ActorCupSizePeriod>();
    public DbSet<MovieActor> MovieActors => Set<MovieActor>();
    public DbSet<CachedImage> CachedImages => Set<CachedImage>();
    public DbSet<ActorImage> ActorImages => Set<ActorImage>();
    public DbSet<ActorAlbum> ActorAlbums => Set<ActorAlbum>();
    public DbSet<ActorPhoto> ActorPhotos => Set<ActorPhoto>();
    public DbSet<JavinizerSettings> JavinizerSettings => Set<JavinizerSettings>();
    public DbSet<ProwlarrSettings> ProwlarrSettings => Set<ProwlarrSettings>();
    public DbSet<JellyfinSettings> JellyfinSettings => Set<JellyfinSettings>();
    public DbSet<LocalLibrarySettings> LocalLibrarySettings => Set<LocalLibrarySettings>();
    public DbSet<ScheduledTaskRun> ScheduledTaskRuns => Set<ScheduledTaskRun>();
    public DbSet<QBittorrentSettings> QBittorrentSettings => Set<QBittorrentSettings>();
    public DbSet<TorrentDownload> TorrentDownloads => Set<TorrentDownload>();
    public DbSet<R18DevSettings> R18DevSettings => Set<R18DevSettings>();
    public DbSet<PathMapping> PathMappings => Set<PathMapping>();
    public DbSet<MediaInfoSettings> MediaInfoSettings => Set<MediaInfoSettings>();
    public DbSet<WarashiSettings> WarashiSettings => Set<WarashiSettings>();
    public DbSet<MinnanoAvSettings> MinnanoAvSettings => Set<MinnanoAvSettings>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<MovieTag> MovieTags => Set<MovieTag>();
    public DbSet<Scene> Scenes => Set<Scene>();
    public DbSet<SceneActor> SceneActors => Set<SceneActor>();
    public DbSet<SceneTag> SceneTags => Set<SceneTag>();
    public DbSet<SceneSuggestion> SceneSuggestions => Set<SceneSuggestion>();
    public DbSet<MovieHighlight> MovieHighlights => Set<MovieHighlight>();
    public DbSet<HighlightTag> HighlightTags => Set<HighlightTag>();
    public DbSet<HighlightActor> HighlightActors => Set<HighlightActor>();
    public DbSet<MovieApex> MovieApexes => Set<MovieApex>();
    public DbSet<ApexTag> ApexTags => Set<ApexTag>();
    public DbSet<ApexActor> ApexActors => Set<ApexActor>();
    public DbSet<SceneEffectiveActor> SceneEffectiveActors => Set<SceneEffectiveActor>();
    public DbSet<HighlightEffectiveActor> HighlightEffectiveActors => Set<HighlightEffectiveActor>();
    public DbSet<ApexEffectiveActor> ApexEffectiveActors => Set<ApexEffectiveActor>();
    public DbSet<MovieActorTag> MovieActorTags => Set<MovieActorTag>();
    public DbSet<SceneActorTag> SceneActorTags => Set<SceneActorTag>();
    public DbSet<HighlightActorTag> HighlightActorTags => Set<HighlightActorTag>();
    public DbSet<ApexActorTag> ApexActorTags => Set<ApexActorTag>();
    public DbSet<SceneEffectiveActorTag> SceneEffectiveActorTags => Set<SceneEffectiveActorTag>();
    public DbSet<HighlightEffectiveActorTag> HighlightEffectiveActorTags => Set<HighlightEffectiveActorTag>();
    public DbSet<ApexEffectiveActorTag> ApexEffectiveActorTags => Set<ApexEffectiveActorTag>();
    public DbSet<TagReplacementRule> TagReplacementRules => Set<TagReplacementRule>();
    public DbSet<IgnoredTag> IgnoredTags => Set<IgnoredTag>();
    public DbSet<TagSettings> TagSettings => Set<TagSettings>();
    public DbSet<TrickplaySettings> TrickplaySettings => Set<TrickplaySettings>();
    public DbSet<ApexPlaybackSettings> ApexPlaybackSettings => Set<ApexPlaybackSettings>();
    public DbSet<DeoVrSettings> DeoVrSettings => Set<DeoVrSettings>();
    public DbSet<DeoVrGroup> DeoVrGroups => Set<DeoVrGroup>();
    public DbSet<TrickplaySet> TrickplaySets => Set<TrickplaySet>();
    public DbSet<DiscoveredMovieCandidate> DiscoveredMovieCandidates => Set<DiscoveredMovieCandidate>();
    public DbSet<NfoGeneration> NfoGenerations => Set<NfoGeneration>();
    public DbSet<DiscoverySourceSetting> DiscoverySourceSettings => Set<DiscoverySourceSetting>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite stores a DateTime without caring about its Kind and reads it back Unspecified. Npgsql's default
        // timestamptz rejects any non-UTC DateTime, and timestamp rejects a UTC one, so on Postgres every DateTime is a
        // plain timestamp whose Kind is dropped on write: both providers then behave the same.
        if (IsPostgres)
        {
            configurationBuilder.Properties<DateTime>()
                .HaveColumnType("timestamp without time zone")
                .HaveConversion<UnspecifiedKindDateTimeConverter>();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (IsPostgres)
        {
            modelBuilder.HasCollation(PostgresCaseInsensitiveCollation, locale: "und-u-ks-level2", provider: "icu", deterministic: false);
            // Trigram indexes for the case-insensitive substring search (see CaseInsensitiveSearch). A trusted extension
            // since PG 13, so the database owner can create it.
            modelBuilder.HasPostgresExtension("pg_trgm");
        }

        modelBuilder.Entity<DeletedMovie>(entity =>
        {
            entity.HasIndex(m => m.NormalizedCode).IsUnique();
            entity.HasIndex(m => m.CanonicalKey);
            entity.HasIndex(m => m.DeletedAt);
            entity.Property(m => m.PreviousStatus).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<Movie>(entity =>
        {
            // NOCASE so a case-insensitive code lookup is a plain equality the unique index can seek.
            // (On Postgres a nondeterministic collation, which ILIKE refuses: CaseInsensitiveSearch casts to "default".)
            entity.Property(m => m.Code).UseCollation(CaseInsensitiveCollation);
            entity.HasIndex(m => m.Code).IsUnique();
            entity.HasIndex(m => m.IsFavorite);
            // Query-only: never written, and never read back after a save (which would add RETURNING to every Movies write).
            var dateAdded = entity.Property<DateTime>(Movie.DateAddedColumn)
                // Postgres can only index a stored generated column.
                .HasComputedColumnSql("COALESCE(\"FileAddedAt\", \"CreatedAt\")", stored: IsPostgres)
                .ValueGeneratedNever();
            dateAdded.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
            dateAdded.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            // (Status, DateAdded) also serves the status counts and Status filters; DateAdded alone the unfiltered sort.
            entity.HasIndex(nameof(Movie.Status), Movie.DateAddedColumn);
            entity.HasIndex(Movie.DateAddedColumn);
            entity.HasIndex(m => m.JellyfinLibraryName);
            if (IsPostgres)
            {
                // ILIKE refuses the nondeterministic collation Code carries, so its search runs as COLLATE "default" and
                // the index is declared with that collation to match.
                entity.HasIndex(m => m.Code, "IX_Movies_Code_Trgm").HasMethod("gin").HasOperators("gin_trgm_ops").UseCollation(CaseInsensitiveSearch.PostgresIndexCollation);
                entity.HasIndex(m => m.MetaTitle, "IX_Movies_MetaTitle_Trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
                entity.HasIndex(m => m.Title, "IX_Movies_Title_Trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
            }
            // A full index, not a partial one on <> 'None': the drift filter's IN (...) can't use the partial one.
            entity.HasIndex(m => m.NfoDriftKind);
            entity.HasIndex(m => m.ClipActorsStale).HasFilter("\"ClipActorsStale\"");
            entity.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(m => m.NfoDriftKind).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<ScheduledTaskRun>(entity =>
        {
            // Each task's latest-ended run (the scheduler and the Tasks page's last execution) and latest-queued run
            // (the Tasks page's run state, and both kept by the history prune).
            entity.HasIndex(r => new { r.TaskName, r.EndedAt });
            entity.HasIndex(r => new { r.TaskName, r.QueuedAt });
        });

        modelBuilder.Entity<DiscoverySourceSetting>(entity => entity.HasIndex(x => x.SourceName).IsUnique());

        modelBuilder.Entity<DeoVrGroup>().HasData(DeoVrGroup.AllMovies);

        modelBuilder.Entity<DiscoveredMovieCandidate>(entity =>
        {
            var stringListComparer = new ValueComparer<List<string>>(
                (left, right) => left != null && right != null ? left.SequenceEqual(right) : left == right,
                urls => urls.Aggregate(0, (hash, url) => HashCode.Combine(hash, StringComparer.Ordinal.GetHashCode(url))),
                urls => urls.ToList());

            entity.HasIndex(c => c.Code).IsUnique();
            entity.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            // The two JSON lists carry no length limit: a compilation lists well over 100 actresses (a dev-database row
            // held 1450 characters against an earlier 500 limit), and Postgres, unlike SQLite, enforces a declared limit.
            entity.Property(c => c.GalleryImageUrls)
                .HasConversion(
                    urls => JsonSerializer.Serialize(urls, (JsonSerializerOptions?)null),
                    json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>())
                .Metadata.SetValueComparer(stringListComparer);
            entity.Property(c => c.ActressNames)
                .HasConversion(
                    names => JsonSerializer.Serialize(names, (JsonSerializerOptions?)null),
                    json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>())
                .Metadata.SetValueComparer(stringListComparer);
        });

        modelBuilder.Entity<Actor>(entity =>
        {
            entity.HasIndex(a => new { a.FirstName, a.LastName }).IsUnique();
            entity.HasIndex(a => a.IsFavorite);
            entity.HasIndex(a => a.CupSize);
            entity.HasIndex(a => a.HeightCm);
            entity.HasIndex(a => a.IsRetired);
        });

        modelBuilder.Entity<ActorAlias>(entity =>
        {
            entity.HasIndex(a => new { a.ActorId, a.Name }).IsUnique();
            entity.HasOne(a => a.Actor)
                .WithMany(actor => actor.Aliases)
                .HasForeignKey(a => a.ActorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ActorCupSizePeriod>(entity =>
        {
            entity.HasIndex(p => new { p.ActorId, p.EffectiveFrom }).IsUnique();
            entity.HasOne(p => p.Actor)
                .WithMany(actor => actor.CupSizePeriods)
                .HasForeignKey(p => p.ActorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MovieActor>(entity =>
        {
            entity.HasKey(movieActor => new { movieActor.MovieId, movieActor.ActorId });
            entity.HasIndex(movieActor => movieActor.ActorId);
            entity.HasOne(movieActor => movieActor.Movie)
                .WithMany(movie => movie.MovieActors)
                .HasForeignKey(movieActor => movieActor.MovieId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(movieActor => movieActor.Actor)
                .WithMany(actor => actor.MovieActors)
                .HasForeignKey(movieActor => movieActor.ActorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MovieFile>(entity =>
        {
            entity.HasIndex(f => new { f.MovieId, f.FileName }).IsUnique();
            entity.HasOne(f => f.Movie)
                .WithMany(m => m.MovieFiles)
                .HasForeignKey(f => f.MovieId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CachedImage>(entity =>
        {
            // Compared with Movie.Code (the grid's poster subquery): Postgres can only use the index below for that
            // comparison when both columns carry the same nondeterministic collation. SQLite compares them as plain text.
            if (IsPostgres)
            {
                entity.Property(c => c.Code).UseCollation(PostgresCaseInsensitiveCollation);
            }

            entity.HasIndex(c => new { c.Code, c.Role, c.Index, c.Variant }).IsUnique();
        });

        modelBuilder.Entity<ActorImage>(entity => entity.HasIndex(a => new { a.ActorId, a.Variant }).IsUnique());

        modelBuilder.Entity<ActorAlbum>(entity =>
        {
            entity.HasIndex(a => new { a.ActorId, a.Name }).IsUnique();
            entity.HasOne(a => a.Actor)
                .WithMany()
                .HasForeignKey(a => a.ActorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ActorPhoto>(entity =>
        {
            entity.HasIndex(a => new { a.ActorId, a.UploadedAt });
            entity.HasOne(a => a.Actor)
                .WithMany()
                .HasForeignKey(a => a.ActorId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(a => a.Album)
                .WithMany(a => a.Photos)
                .HasForeignKey(a => a.AlbumId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TorrentDownload>(entity =>
        {
            entity.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(t => t.Hash);
        });

        modelBuilder.Entity<PathMapping>(entity => entity.HasIndex(p => p.QBittorrentPrefix).IsUnique());

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.HasIndex(t => t.Name).IsUnique();
            entity.HasIndex(t => t.ParentTagId);
            entity.HasOne(t => t.ParentTag)
                .WithMany(t => t.Subtags)
                .HasForeignKey(t => t.ParentTagId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MovieTag>(entity =>
        {
            entity.HasKey(movieTag => new { movieTag.MovieId, movieTag.TagId });
            entity.HasIndex(movieTag => movieTag.TagId);
            entity.HasOne(movieTag => movieTag.Movie)
                .WithMany(movie => movie.MovieTags)
                .HasForeignKey(movieTag => movieTag.MovieId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(movieTag => movieTag.Tag)
                .WithMany(tag => tag.MovieTags)
                .HasForeignKey(movieTag => movieTag.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Scene>(entity =>
        {
            entity.HasIndex(scene => new { scene.MovieId, scene.StartSeconds });
            entity.HasIndex(scene => scene.IsFavorite);
            entity.HasOne(scene => scene.Movie)
                .WithMany(movie => movie.Scenes)
                .HasForeignKey(scene => scene.MovieId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MovieHighlight>(entity =>
        {
            entity.HasIndex(highlight => new { highlight.MovieId, highlight.StartSeconds });
            entity.HasOne(highlight => highlight.Movie)
                .WithMany(movie => movie.Highlights)
                .HasForeignKey(highlight => highlight.MovieId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MovieApex>(entity =>
        {
            entity.HasIndex(apex => new { apex.MovieId, apex.Seconds });
            entity.HasOne(apex => apex.Movie)
                .WithMany(movie => movie.Apexes)
                .HasForeignKey(apex => apex.MovieId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SceneActor>(entity =>
        {
            entity.HasKey(sceneActor => new { sceneActor.SceneId, sceneActor.ActorId });
            entity.HasIndex(sceneActor => new { sceneActor.MovieId, sceneActor.ActorId });
            entity.HasOne(sceneActor => sceneActor.Scene)
                .WithMany(scene => scene.SceneActors)
                .HasForeignKey(sceneActor => sceneActor.SceneId)
                .OnDelete(DeleteBehavior.Cascade);
            // Points at the movie's cast link, so leaving the cast leaves the scenes (DB cascade).
            entity.HasOne(sceneActor => sceneActor.MovieActor)
                .WithMany()
                .HasForeignKey(sceneActor => new { sceneActor.MovieId, sceneActor.ActorId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SceneSuggestion>(entity =>
        {
            entity.HasIndex(suggestion => suggestion.MovieId);
            entity.Property(suggestion => suggestion.Source).HasConversion<string>().HasMaxLength(10);
            entity.HasOne(suggestion => suggestion.Movie)
                .WithMany()
                .HasForeignKey(suggestion => suggestion.MovieId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SceneTag>(entity =>
        {
            entity.HasKey(sceneTag => new { sceneTag.SceneId, sceneTag.TagId });
            entity.HasIndex(sceneTag => sceneTag.TagId);
            entity.HasOne(sceneTag => sceneTag.Scene)
                .WithMany(scene => scene.SceneTags)
                .HasForeignKey(sceneTag => sceneTag.SceneId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(sceneTag => sceneTag.Tag)
                .WithMany()
                .HasForeignKey(sceneTag => sceneTag.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApexTag>(entity =>
        {
            entity.HasKey(apexTag => new { apexTag.ApexId, apexTag.TagId });
            entity.HasIndex(apexTag => apexTag.TagId);
            entity.HasOne(apexTag => apexTag.Apex)
                .WithMany(apex => apex.ApexTags)
                .HasForeignKey(apexTag => apexTag.ApexId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(apexTag => apexTag.Tag)
                .WithMany()
                .HasForeignKey(apexTag => apexTag.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApexActor>(entity =>
        {
            entity.HasKey(apexActor => new { apexActor.ApexId, apexActor.ActorId });
            entity.HasIndex(apexActor => new { apexActor.MovieId, apexActor.ActorId });
            entity.HasOne(apexActor => apexActor.Apex)
                .WithMany(apex => apex.ApexActors)
                .HasForeignKey(apexActor => apexActor.ApexId)
                .OnDelete(DeleteBehavior.Cascade);
            // Points at the movie's cast link, so leaving the cast leaves the apexes (DB cascade).
            entity.HasOne(apexActor => apexActor.MovieActor)
                .WithMany()
                .HasForeignKey(apexActor => new { apexActor.MovieId, apexActor.ActorId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HighlightTag>(entity =>
        {
            entity.HasKey(highlightTag => new { highlightTag.HighlightId, highlightTag.TagId });
            entity.HasIndex(highlightTag => highlightTag.TagId);
            entity.HasOne(highlightTag => highlightTag.Highlight)
                .WithMany(highlight => highlight.HighlightTags)
                .HasForeignKey(highlightTag => highlightTag.HighlightId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(highlightTag => highlightTag.Tag)
                .WithMany()
                .HasForeignKey(highlightTag => highlightTag.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HighlightActor>(entity =>
        {
            entity.HasKey(highlightActor => new { highlightActor.HighlightId, highlightActor.ActorId });
            entity.HasIndex(highlightActor => new { highlightActor.MovieId, highlightActor.ActorId });
            entity.HasOne(highlightActor => highlightActor.Highlight)
                .WithMany(highlight => highlight.HighlightActors)
                .HasForeignKey(highlightActor => highlightActor.HighlightId)
                .OnDelete(DeleteBehavior.Cascade);
            // Points at the movie's cast link, so leaving the cast leaves the highlights (DB cascade).
            entity.HasOne(highlightActor => highlightActor.MovieActor)
                .WithMany()
                .HasForeignKey(highlightActor => new { highlightActor.MovieId, highlightActor.ActorId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Stored effective actors, keyed like the own-actor links; the (ActorId, owner) index
        // serves the wall's Actor filter, EF's (MovieId, ActorId) FK index the actress-attribute options.
        modelBuilder.Entity<SceneEffectiveActor>(entity =>
        {
            entity.HasKey(row => new { row.SceneId, row.ActorId });
            entity.HasIndex(row => new { row.ActorId, row.SceneId });
            entity.HasOne(row => row.Scene)
                .WithMany()
                .HasForeignKey(row => row.SceneId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(row => row.MovieActor)
                .WithMany()
                .HasForeignKey(row => new { row.MovieId, row.ActorId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HighlightEffectiveActor>(entity =>
        {
            entity.HasKey(row => new { row.HighlightId, row.ActorId });
            entity.HasIndex(row => new { row.ActorId, row.HighlightId });
            entity.HasOne(row => row.Highlight)
                .WithMany()
                .HasForeignKey(row => row.HighlightId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(row => row.MovieActor)
                .WithMany()
                .HasForeignKey(row => new { row.MovieId, row.ActorId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApexEffectiveActor>(entity =>
        {
            entity.HasKey(row => new { row.ApexId, row.ActorId });
            entity.HasIndex(row => new { row.ActorId, row.ApexId });
            entity.HasOne(row => row.Apex)
                .WithMany()
                .HasForeignKey(row => row.ApexId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(row => row.MovieActor)
                .WithMany()
                .HasForeignKey(row => new { row.MovieId, row.ActorId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Actor-scoped tags: a tag on one actor within a movie, scene, highlight or apex (Tag.IsActorTag), and the
        // stored effective ones ClipActorSync keeps for the wall's filters. The (TagId, ...) index serves those filters.
        modelBuilder.Entity<MovieActorTag>(entity =>
        {
            entity.HasKey(row => new { row.MovieId, row.ActorId, row.TagId });
            entity.HasIndex(row => new { row.TagId, row.ActorId, row.MovieId });
            entity.HasOne(row => row.MovieActor)
                .WithMany()
                .HasForeignKey(row => new { row.MovieId, row.ActorId })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(row => row.Tag)
                .WithMany()
                .HasForeignKey(row => row.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SceneActorTag>(entity =>
        {
            entity.HasKey(row => new { row.SceneId, row.ActorId, row.TagId });
            entity.HasIndex(row => new { row.TagId, row.ActorId, row.SceneId });
            entity.HasOne(row => row.Scene)
                .WithMany()
                .HasForeignKey(row => row.SceneId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(row => row.MovieActor)
                .WithMany()
                .HasForeignKey(row => new { row.MovieId, row.ActorId })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(row => row.Tag)
                .WithMany()
                .HasForeignKey(row => row.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HighlightActorTag>(entity =>
        {
            entity.HasKey(row => new { row.HighlightId, row.ActorId, row.TagId });
            entity.HasIndex(row => new { row.TagId, row.ActorId, row.HighlightId });
            entity.HasOne(row => row.Highlight)
                .WithMany()
                .HasForeignKey(row => row.HighlightId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(row => row.MovieActor)
                .WithMany()
                .HasForeignKey(row => new { row.MovieId, row.ActorId })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(row => row.Tag)
                .WithMany()
                .HasForeignKey(row => row.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApexActorTag>(entity =>
        {
            entity.HasKey(row => new { row.ApexId, row.ActorId, row.TagId });
            entity.HasIndex(row => new { row.TagId, row.ActorId, row.ApexId });
            entity.HasOne(row => row.Apex)
                .WithMany()
                .HasForeignKey(row => row.ApexId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(row => row.MovieActor)
                .WithMany()
                .HasForeignKey(row => new { row.MovieId, row.ActorId })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(row => row.Tag)
                .WithMany()
                .HasForeignKey(row => row.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SceneEffectiveActorTag>(entity =>
        {
            entity.HasKey(row => new { row.SceneId, row.ActorId, row.TagId });
            entity.HasIndex(row => new { row.TagId, row.ActorId, row.SceneId });
            entity.HasOne(row => row.Scene)
                .WithMany()
                .HasForeignKey(row => row.SceneId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(row => row.MovieActor)
                .WithMany()
                .HasForeignKey(row => new { row.MovieId, row.ActorId })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(row => row.Tag)
                .WithMany()
                .HasForeignKey(row => row.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HighlightEffectiveActorTag>(entity =>
        {
            entity.HasKey(row => new { row.HighlightId, row.ActorId, row.TagId });
            entity.HasIndex(row => new { row.TagId, row.ActorId, row.HighlightId });
            entity.HasOne(row => row.Highlight)
                .WithMany()
                .HasForeignKey(row => row.HighlightId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(row => row.MovieActor)
                .WithMany()
                .HasForeignKey(row => new { row.MovieId, row.ActorId })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(row => row.Tag)
                .WithMany()
                .HasForeignKey(row => row.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApexEffectiveActorTag>(entity =>
        {
            entity.HasKey(row => new { row.ApexId, row.ActorId, row.TagId });
            entity.HasIndex(row => new { row.TagId, row.ActorId, row.ApexId });
            entity.HasOne(row => row.Apex)
                .WithMany()
                .HasForeignKey(row => row.ApexId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(row => row.MovieActor)
                .WithMany()
                .HasForeignKey(row => new { row.MovieId, row.ActorId })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(row => row.Tag)
                .WithMany()
                .HasForeignKey(row => row.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TagReplacementRule>(entity =>
        {
            entity.Property(r => r.MatchMode).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(r => new { r.SourceValue, r.MatchMode }).IsUnique();
            entity.HasOne(r => r.TargetTag)
                .WithMany()
                .HasForeignKey(r => r.TargetTagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IgnoredTag>(entity =>
        {
            entity.Property(i => i.MatchMode).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(i => new { i.Value, i.MatchMode }).IsUnique();
        });

        modelBuilder.Entity<NfoGeneration>(entity =>
        {
            entity.Property(g => g.Trigger).HasConversion<string>().HasMaxLength(50);
            entity.HasIndex(g => new { g.MovieId, g.ReplacedAtUtc });
            entity.HasOne(g => g.Movie)
                .WithMany()
                .HasForeignKey(g => g.MovieId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrickplaySet>(entity => entity.HasIndex(set => new { set.CodeFolder, set.Identity }).IsUnique());

        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>Drops a DateTime's Kind on write (see <see cref="AppDbContext"/>'s conventions); the value read back is Unspecified.</summary>
internal sealed class UnspecifiedKindDateTimeConverter()
    : ValueConverter<DateTime, DateTime>(v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified), v => v);
