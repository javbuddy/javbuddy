using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Javbuddy.Migrations.Postgres;

/// <inheritdoc />
public partial class _20261023193122_Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:CollationDefinition:case_insensitive", "und-u-ks-level2,und-u-ks-level2,icu,False");

        migrationBuilder.CreateTable(
            name: "ActorImages",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ActorId = table.Column<int>(type: "integer", nullable: false),
                SourceMovieCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Variant = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                StorageId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceStorageId = table.Column<Guid>(type: "uuid", nullable: true),
                SourceExtension = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                CropX = table.Column<double>(type: "double precision", nullable: true),
                CropY = table.Column<double>(type: "double precision", nullable: true),
                CropWidth = table.Column<double>(type: "double precision", nullable: true),
                CropHeight = table.Column<double>(type: "double precision", nullable: true),
                SourceLength = table.Column<long>(type: "bigint", nullable: false),
                SourceLastWriteUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                SourceUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ActorImages", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Actors",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                FirstName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                LastName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                JapaneseNameKanji = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                JapaneseNameKana = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                JellyfinPersonId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                R18DevId = table.Column<int>(type: "integer", nullable: true),
                R18DevName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                IsFavorite = table.Column<bool>(type: "boolean", nullable: false),
                FavoritedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                HeightCm = table.Column<int>(type: "integer", nullable: true),
                CupSize = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                Bust = table.Column<int>(type: "integer", nullable: true),
                Waist = table.Column<int>(type: "integer", nullable: true),
                Hips = table.Column<int>(type: "integer", nullable: true),
                BirthDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                IsRetired = table.Column<bool>(type: "boolean", nullable: false),
                IsTransgender = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Actors", x => x.Id));

        migrationBuilder.CreateTable(
            name: "ApexPlaybackSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                LeadInSeconds = table.Column<double>(type: "double precision", nullable: false),
                TailSeconds = table.Column<double>(type: "double precision", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ApexPlaybackSettings", x => x.Id));

        migrationBuilder.CreateTable(
            name: "CachedImages",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Index = table.Column<int>(type: "integer", nullable: false),
                Variant = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                StorageId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceLength = table.Column<long>(type: "bigint", nullable: false),
                SourceLastWriteUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                SourceUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                SceneStartMs = table.Column<long>(type: "bigint", nullable: true),
                SceneEndMs = table.Column<long>(type: "bigint", nullable: true),
                SourceIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_CachedImages", x => x.Id));

        migrationBuilder.CreateTable(
            name: "DeletedMovies",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                NormalizedCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                CanonicalKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                MetaTitle = table.Column<string>(type: "text", nullable: true),
                DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                PreviousStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_DeletedMovies", x => x.Id));

        migrationBuilder.CreateTable(
            name: "DeoVrGroups",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Position = table.Column<int>(type: "integer", nullable: false),
                FilterJson = table.Column<string>(type: "text", nullable: false),
                SortField = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                SortDescending = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_DeoVrGroups", x => x.Id));

        migrationBuilder.CreateTable(
            name: "DeoVrSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Enabled = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_DeoVrSettings", x => x.Id));

        migrationBuilder.CreateTable(
            name: "DiscoveredMovieCandidates",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Studio = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                SourceName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                CoverImageUrl = table.Column<string>(type: "text", nullable: true),
                GalleryImageUrls = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                ActressNames = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                ReleaseDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                IsUpcoming = table.Column<bool>(type: "boolean", nullable: false),
                FirstSeenAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                LastSeenAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_DiscoveredMovieCandidates", x => x.Id));

        migrationBuilder.CreateTable(
            name: "DiscoverySourceSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                SourceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Enabled = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_DiscoverySourceSettings", x => x.Id));

        migrationBuilder.CreateTable(
            name: "IgnoredTags",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                MatchMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_IgnoredTags", x => x.Id));

        migrationBuilder.CreateTable(
            name: "JavinizerSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                ExternalUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                ApiToken = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_JavinizerSettings", x => x.Id));

        migrationBuilder.CreateTable(
            name: "JellyfinSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Enabled = table.Column<bool>(type: "boolean", nullable: false),
                BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                ExternalUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                ApiKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                SelectedLibraryNames = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                LastLinkSyncAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                LastLinkSyncChecked = table.Column<int>(type: "integer", nullable: true),
                LastLinkSyncMatched = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_JellyfinSettings", x => x.Id));

        migrationBuilder.CreateTable(
            name: "LocalLibrarySettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                RootPaths = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_LocalLibrarySettings", x => x.Id));

        migrationBuilder.CreateTable(
            name: "MediaInfoSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Enabled = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_MediaInfoSettings", x => x.Id));

        migrationBuilder.CreateTable(
            name: "MinnanoAvSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Enabled = table.Column<bool>(type: "boolean", nullable: false),
                BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                RequestDelayMs = table.Column<int>(type: "integer", nullable: false),
                OverwriteExisting = table.Column<bool>(type: "boolean", nullable: false),
                LastEnrichedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                LastEnrichSummary = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_MinnanoAvSettings", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Movies",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                FileCount = table.Column<int>(type: "integer", nullable: false),
                VrType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, collation: "case_insensitive"),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                MetaTitle = table.Column<string>(type: "text", nullable: true),
                MetaOriginalTitle = table.Column<string>(type: "text", nullable: true),
                MetaDescription = table.Column<string>(type: "text", nullable: true),
                MetaReleaseDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                MetaDirector = table.Column<string>(type: "text", nullable: true),
                MetaStudio = table.Column<string>(type: "text", nullable: true),
                MetaLabel = table.Column<string>(type: "text", nullable: true),
                MetaSeries = table.Column<string>(type: "text", nullable: true),
                MetaRatingScore = table.Column<double>(type: "double precision", nullable: true),
                MetaRatingVotes = table.Column<int>(type: "integer", nullable: true),
                MetaCoverUrl = table.Column<string>(type: "text", nullable: true),
                MetaBackdropUrl = table.Column<string>(type: "text", nullable: true),
                MetaRuntimeMinutes = table.Column<int>(type: "integer", nullable: true),
                MetaActresses = table.Column<string>(type: "text", nullable: true),
                MetaGenres = table.Column<string>(type: "text", nullable: true),
                MetaSourceName = table.Column<string>(type: "text", nullable: true),
                MetaSourceUrl = table.Column<string>(type: "text", nullable: true),
                MetaFetchedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                JellyfinItemId = table.Column<string>(type: "text", nullable: true),
                JellyfinServerId = table.Column<string>(type: "text", nullable: true),
                JellyfinLibraryId = table.Column<string>(type: "text", nullable: true),
                JellyfinLibraryName = table.Column<string>(type: "text", nullable: true),
                JellyfinCheckedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                LocalFileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                MediaVideoFileLastWriteUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                MediaNfoLastWriteUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                NfoDriftKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                NfoConflictDetails = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                NfoBaselineJson = table.Column<string>(type: "text", nullable: true),
                NfoBaselineLastWriteUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                NfoBaselineSize = table.Column<long>(type: "bigint", nullable: true),
                HasUnmatchedActors = table.Column<bool>(type: "boolean", nullable: false),
                UnmatchedActorNames = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                FileAddedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                CleanupBlacklisted = table.Column<bool>(type: "boolean", nullable: false),
                CleanupSnoozedUntil = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                LastReviewedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                HasBrokenBFrames = table.Column<bool>(type: "boolean", nullable: false),
                IsFavorite = table.Column<bool>(type: "boolean", nullable: false),
                FavoritedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                ClipActorsStale = table.Column<bool>(type: "boolean", nullable: false),
                MediaImagesSignature = table.Column<string>(type: "text", nullable: true),
                MediaVideoFileName = table.Column<string>(type: "text", nullable: true),
                MediaContainerFormat = table.Column<string>(type: "text", nullable: true),
                MediaContainerFormatProfile = table.Column<string>(type: "text", nullable: true),
                MediaContainerCodecId = table.Column<string>(type: "text", nullable: true),
                MediaContainerCodecIdCompatible = table.Column<string>(type: "text", nullable: true),
                MediaWritingApplication = table.Column<string>(type: "text", nullable: true),
                MediaDurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                MediaOverallBitRateKbps = table.Column<int>(type: "integer", nullable: true),
                MediaVideoStreamId = table.Column<string>(type: "text", nullable: true),
                MediaVideoCodec = table.Column<string>(type: "text", nullable: true),
                MediaVideoFormatInfo = table.Column<string>(type: "text", nullable: true),
                MediaVideoProfile = table.Column<string>(type: "text", nullable: true),
                MediaVideoFormatSettings = table.Column<string>(type: "text", nullable: true),
                MediaVideoCodecId = table.Column<string>(type: "text", nullable: true),
                MediaVideoCodecIdInfo = table.Column<string>(type: "text", nullable: true),
                MediaWidth = table.Column<int>(type: "integer", nullable: true),
                MediaHeight = table.Column<int>(type: "integer", nullable: true),
                MediaAspectRatio = table.Column<string>(type: "text", nullable: true),
                MediaFrameRate = table.Column<double>(type: "double precision", nullable: true),
                MediaFrameRateMode = table.Column<string>(type: "text", nullable: true),
                MediaFrameRateMin = table.Column<double>(type: "double precision", nullable: true),
                MediaFrameRateMax = table.Column<double>(type: "double precision", nullable: true),
                MediaVideoBitRateKbps = table.Column<int>(type: "integer", nullable: true),
                MediaColorSpace = table.Column<string>(type: "text", nullable: true),
                MediaChromaSubsampling = table.Column<string>(type: "text", nullable: true),
                MediaBitDepth = table.Column<int>(type: "integer", nullable: true),
                MediaScanType = table.Column<string>(type: "text", nullable: true),
                MediaBitsPerPixelFrame = table.Column<double>(type: "double precision", nullable: true),
                MediaVideoStreamSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                MediaWritingLibrary = table.Column<string>(type: "text", nullable: true),
                MediaEncodingSettings = table.Column<string>(type: "text", nullable: true),
                MediaColorRange = table.Column<string>(type: "text", nullable: true),
                MediaColorPrimaries = table.Column<string>(type: "text", nullable: true),
                MediaTransferCharacteristics = table.Column<string>(type: "text", nullable: true),
                MediaMatrixCoefficients = table.Column<string>(type: "text", nullable: true),
                MediaCodecConfigurationBox = table.Column<string>(type: "text", nullable: true),
                MediaAudioStreamId = table.Column<string>(type: "text", nullable: true),
                MediaAudioCodec = table.Column<string>(type: "text", nullable: true),
                MediaAudioFormatInfo = table.Column<string>(type: "text", nullable: true),
                MediaAudioFormatSettings = table.Column<string>(type: "text", nullable: true),
                MediaAudioCodecId = table.Column<string>(type: "text", nullable: true),
                MediaAudioBitRateMode = table.Column<string>(type: "text", nullable: true),
                MediaAudioBitRateKbps = table.Column<int>(type: "integer", nullable: true),
                MediaAudioChannels = table.Column<int>(type: "integer", nullable: true),
                MediaAudioChannelLayout = table.Column<string>(type: "text", nullable: true),
                MediaAudioSamplingRateHz = table.Column<int>(type: "integer", nullable: true),
                MediaAudioFrameRate = table.Column<double>(type: "double precision", nullable: true),
                MediaAudioCompressionMode = table.Column<string>(type: "text", nullable: true),
                MediaAudioStreamSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                MediaAudioDefault = table.Column<bool>(type: "boolean", nullable: true),
                MediaAudioAlternateGroup = table.Column<int>(type: "integer", nullable: true),
                MediaSubtitleCount = table.Column<int>(type: "integer", nullable: true),
                MediaHasSubtitleFile = table.Column<bool>(type: "boolean", nullable: false),
                MediaHasTrailerFile = table.Column<bool>(type: "boolean", nullable: false),
                MediaScannedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                MediaScanError = table.Column<string>(type: "text", nullable: true),
                MediaScanWarning = table.Column<string>(type: "text", nullable: true),
                DateAdded = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, computedColumnSql: "COALESCE(\"FileAddedAt\", \"CreatedAt\")", stored: true)
            },
            constraints: table => table.PrimaryKey("PK_Movies", x => x.Id));

        migrationBuilder.CreateTable(
            name: "PathMappings",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                QBittorrentPrefix = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                JavinizerPrefix = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                AppPrefix = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_PathMappings", x => x.Id));

        migrationBuilder.CreateTable(
            name: "ProwlarrSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                ExternalUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                ApiKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ProwlarrSettings", x => x.Id));

        migrationBuilder.CreateTable(
            name: "QBittorrentSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                ExternalUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                Username = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Password = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_QBittorrentSettings", x => x.Id));

        migrationBuilder.CreateTable(
            name: "R18DevSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Enabled = table.Column<bool>(type: "boolean", nullable: false),
                DumpSourceOverride = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                LastImportedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                LastImportSourceDate = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                LastImportSummary = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_R18DevSettings", x => x.Id));

        migrationBuilder.CreateTable(
            name: "ScheduledTaskRuns",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                TaskName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                QueuedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                StartedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                EndedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                Success = table.Column<bool>(type: "boolean", nullable: false),
                ProgressCurrent = table.Column<int>(type: "integer", nullable: true),
                ProgressTotal = table.Column<int>(type: "integer", nullable: true),
                ProgressStage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                ResultSummary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ScheduledTaskRuns", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Tags",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ParentTagId = table.Column<int>(type: "integer", nullable: true),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                NeedsReview = table.Column<bool>(type: "boolean", nullable: false),
                IsActorTag = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Tags", x => x.Id);
                table.ForeignKey(
                    name: "FK_Tags_Tags_ParentTagId",
                    column: x => x.ParentTagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "TagSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                AutoIgnoreNonLatinTags = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_TagSettings", x => x.Id));

        migrationBuilder.CreateTable(
            name: "TrickplaySets",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                CodeFolder = table.Column<string>(type: "text", nullable: false),
                Identity = table.Column<string>(type: "text", nullable: false),
                Width = table.Column<int>(type: "integer", nullable: false),
                Height = table.Column<int>(type: "integer", nullable: false),
                TileWidth = table.Column<int>(type: "integer", nullable: false),
                TileHeight = table.Column<int>(type: "integer", nullable: false),
                ThumbnailCount = table.Column<int>(type: "integer", nullable: false),
                IntervalMs = table.Column<int>(type: "integer", nullable: false),
                DurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                LeftEyeOnly = table.Column<bool>(type: "boolean", nullable: false),
                KeyframeOnly = table.Column<bool>(type: "boolean", nullable: false),
                GeneratedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                FileName = table.Column<string>(type: "text", nullable: false),
                SourceIdentity = table.Column<string>(type: "text", nullable: true),
                StartSeconds = table.Column<double>(type: "double precision", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_TrickplaySets", x => x.Id));

        migrationBuilder.CreateTable(
            name: "TrickplaySettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                GenerateForNewFiles = table.Column<bool>(type: "boolean", nullable: false),
                KeyframeOnly = table.Column<bool>(type: "boolean", nullable: false),
                JellyfinFallback = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_TrickplaySettings", x => x.Id));

        migrationBuilder.CreateTable(
            name: "WarashiSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Enabled = table.Column<bool>(type: "boolean", nullable: false),
                BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                RequestDelayMs = table.Column<int>(type: "integer", nullable: false),
                OverwriteExisting = table.Column<bool>(type: "boolean", nullable: false),
                LastEnrichedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                LastEnrichSummary = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_WarashiSettings", x => x.Id));

        migrationBuilder.CreateTable(
            name: "ActorAlbums",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ActorId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ActorAlbums", x => x.Id);
                table.ForeignKey(
                    name: "FK_ActorAlbums_Actors_ActorId",
                    column: x => x.ActorId,
                    principalTable: "Actors",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ActorAliases",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ActorId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ActorAliases", x => x.Id);
                table.ForeignKey(
                    name: "FK_ActorAliases_Actors_ActorId",
                    column: x => x.ActorId,
                    principalTable: "Actors",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ActorCupSizePeriods",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ActorId = table.Column<int>(type: "integer", nullable: false),
                EffectiveFrom = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                CupSize = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ActorCupSizePeriods", x => x.Id);
                table.ForeignKey(
                    name: "FK_ActorCupSizePeriods_Actors_ActorId",
                    column: x => x.ActorId,
                    principalTable: "Actors",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MovieActors",
            columns: table => new
            {
                MovieId = table.Column<int>(type: "integer", nullable: false),
                ActorId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MovieActors", x => new { x.MovieId, x.ActorId });
                table.ForeignKey(
                    name: "FK_MovieActors_Actors_ActorId",
                    column: x => x.ActorId,
                    principalTable: "Actors",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_MovieActors_Movies_MovieId",
                    column: x => x.MovieId,
                    principalTable: "Movies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MovieApexes",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MovieId = table.Column<int>(type: "integer", nullable: false),
                Seconds = table.Column<double>(type: "double precision", nullable: false),
                LeadInSeconds = table.Column<double>(type: "double precision", nullable: true),
                TailSeconds = table.Column<double>(type: "double precision", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                IsFavorite = table.Column<bool>(type: "boolean", nullable: false),
                FavoritedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MovieApexes", x => x.Id);
                table.ForeignKey(
                    name: "FK_MovieApexes_Movies_MovieId",
                    column: x => x.MovieId,
                    principalTable: "Movies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MovieFiles",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MovieId = table.Column<int>(type: "integer", nullable: false),
                FileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                VersionTag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                FileAddedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                LastWriteUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                IsPrimaryPinned = table.Column<bool>(type: "boolean", nullable: false),
                VrType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                VrTypePinned = table.Column<bool>(type: "boolean", nullable: false),
                ContainerFormat = table.Column<string>(type: "text", nullable: true),
                ContainerFormatProfile = table.Column<string>(type: "text", nullable: true),
                ContainerCodecId = table.Column<string>(type: "text", nullable: true),
                ContainerCodecIdCompatible = table.Column<string>(type: "text", nullable: true),
                WritingApplication = table.Column<string>(type: "text", nullable: true),
                DurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                OverallBitRateKbps = table.Column<int>(type: "integer", nullable: true),
                VideoStreamId = table.Column<string>(type: "text", nullable: true),
                VideoCodec = table.Column<string>(type: "text", nullable: true),
                VideoFormatInfo = table.Column<string>(type: "text", nullable: true),
                VideoProfile = table.Column<string>(type: "text", nullable: true),
                VideoFormatSettings = table.Column<string>(type: "text", nullable: true),
                VideoCodecId = table.Column<string>(type: "text", nullable: true),
                VideoCodecIdInfo = table.Column<string>(type: "text", nullable: true),
                Width = table.Column<int>(type: "integer", nullable: true),
                Height = table.Column<int>(type: "integer", nullable: true),
                AspectRatio = table.Column<string>(type: "text", nullable: true),
                FrameRate = table.Column<double>(type: "double precision", nullable: true),
                FrameRateMode = table.Column<string>(type: "text", nullable: true),
                FrameRateMin = table.Column<double>(type: "double precision", nullable: true),
                FrameRateMax = table.Column<double>(type: "double precision", nullable: true),
                VideoBitRateKbps = table.Column<int>(type: "integer", nullable: true),
                ColorSpace = table.Column<string>(type: "text", nullable: true),
                ChromaSubsampling = table.Column<string>(type: "text", nullable: true),
                BitDepth = table.Column<int>(type: "integer", nullable: true),
                ScanType = table.Column<string>(type: "text", nullable: true),
                BitsPerPixelFrame = table.Column<double>(type: "double precision", nullable: true),
                VideoStreamSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                WritingLibrary = table.Column<string>(type: "text", nullable: true),
                EncodingSettings = table.Column<string>(type: "text", nullable: true),
                ColorRange = table.Column<string>(type: "text", nullable: true),
                ColorPrimaries = table.Column<string>(type: "text", nullable: true),
                TransferCharacteristics = table.Column<string>(type: "text", nullable: true),
                MatrixCoefficients = table.Column<string>(type: "text", nullable: true),
                CodecConfigurationBox = table.Column<string>(type: "text", nullable: true),
                AudioStreamId = table.Column<string>(type: "text", nullable: true),
                AudioCodec = table.Column<string>(type: "text", nullable: true),
                AudioFormatInfo = table.Column<string>(type: "text", nullable: true),
                AudioFormatSettings = table.Column<string>(type: "text", nullable: true),
                AudioCodecId = table.Column<string>(type: "text", nullable: true),
                AudioBitRateMode = table.Column<string>(type: "text", nullable: true),
                AudioBitRateKbps = table.Column<int>(type: "integer", nullable: true),
                AudioChannels = table.Column<int>(type: "integer", nullable: true),
                AudioChannelLayout = table.Column<string>(type: "text", nullable: true),
                AudioSamplingRateHz = table.Column<int>(type: "integer", nullable: true),
                AudioFrameRate = table.Column<double>(type: "double precision", nullable: true),
                AudioCompressionMode = table.Column<string>(type: "text", nullable: true),
                AudioStreamSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                AudioDefault = table.Column<bool>(type: "boolean", nullable: true),
                AudioAlternateGroup = table.Column<int>(type: "integer", nullable: true),
                SubtitleCount = table.Column<int>(type: "integer", nullable: true),
                MediaScannedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                MediaScanError = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MovieFiles", x => x.Id);
                table.ForeignKey(
                    name: "FK_MovieFiles_Movies_MovieId",
                    column: x => x.MovieId,
                    principalTable: "Movies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MovieHighlights",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MovieId = table.Column<int>(type: "integer", nullable: false),
                StartSeconds = table.Column<double>(type: "double precision", nullable: false),
                EndSeconds = table.Column<double>(type: "double precision", nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                IsFavorite = table.Column<bool>(type: "boolean", nullable: false),
                FavoritedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MovieHighlights", x => x.Id);
                table.ForeignKey(
                    name: "FK_MovieHighlights_Movies_MovieId",
                    column: x => x.MovieId,
                    principalTable: "Movies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "NfoGenerations",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MovieId = table.Column<int>(type: "integer", nullable: false),
                ReplacedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                Trigger = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Content = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NfoGenerations", x => x.Id);
                table.ForeignKey(
                    name: "FK_NfoGenerations_Movies_MovieId",
                    column: x => x.MovieId,
                    principalTable: "Movies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Scenes",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MovieId = table.Column<int>(type: "integer", nullable: false),
                StartSeconds = table.Column<double>(type: "double precision", nullable: false),
                EndSeconds = table.Column<double>(type: "double precision", nullable: true),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                IsFavorite = table.Column<bool>(type: "boolean", nullable: false),
                FavoritedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                IsHiddenFromOverview = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Scenes", x => x.Id);
                table.ForeignKey(
                    name: "FK_Scenes_Movies_MovieId",
                    column: x => x.MovieId,
                    principalTable: "Movies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SceneSuggestions",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MovieId = table.Column<int>(type: "integer", nullable: false),
                Seconds = table.Column<double>(type: "double precision", nullable: false),
                Source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Score = table.Column<double>(type: "double precision", nullable: false),
                Dismissed = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SceneSuggestions", x => x.Id);
                table.ForeignKey(
                    name: "FK_SceneSuggestions_Movies_MovieId",
                    column: x => x.MovieId,
                    principalTable: "Movies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "TorrentDownloads",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MovieId = table.Column<int>(type: "integer", nullable: false),
                MovieCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ReleaseTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                Indexer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Size = table.Column<long>(type: "bigint", nullable: true),
                SourceUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                SavePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ContentPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                JavinizerBatchJobId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                SortedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Progress = table.Column<double>(type: "double precision", nullable: true),
                DownloadSpeedBytesPerSec = table.Column<long>(type: "bigint", nullable: true),
                EtaSeconds = table.Column<long>(type: "bigint", nullable: true),
                GrabbedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                CompletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                RemovedFromClientAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TorrentDownloads", x => x.Id);
                table.ForeignKey(
                    name: "FK_TorrentDownloads_Movies_MovieId",
                    column: x => x.MovieId,
                    principalTable: "Movies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MovieTags",
            columns: table => new
            {
                MovieId = table.Column<int>(type: "integer", nullable: false),
                TagId = table.Column<int>(type: "integer", nullable: false),
                IsExplicit = table.Column<bool>(type: "boolean", nullable: false),
                FromClips = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MovieTags", x => new { x.MovieId, x.TagId });
                table.ForeignKey(
                    name: "FK_MovieTags_Movies_MovieId",
                    column: x => x.MovieId,
                    principalTable: "Movies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_MovieTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "TagReplacementRules",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                SourceValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                MatchMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                TargetTagId = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TagReplacementRules", x => x.Id);
                table.ForeignKey(
                    name: "FK_TagReplacementRules_Tags_TargetTagId",
                    column: x => x.TargetTagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ActorPhotos",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ActorId = table.Column<int>(type: "integer", nullable: false),
                AlbumId = table.Column<int>(type: "integer", nullable: true),
                ThumbStorageId = table.Column<Guid>(type: "uuid", nullable: false),
                FullStorageId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceStorageId = table.Column<Guid>(type: "uuid", nullable: true),
                SourceExtension = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                UploadedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                AspectRatio = table.Column<double>(type: "double precision", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ActorPhotos", x => x.Id);
                table.ForeignKey(
                    name: "FK_ActorPhotos_ActorAlbums_AlbumId",
                    column: x => x.AlbumId,
                    principalTable: "ActorAlbums",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_ActorPhotos_Actors_ActorId",
                    column: x => x.ActorId,
                    principalTable: "Actors",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MovieActorTags",
            columns: table => new
            {
                MovieId = table.Column<int>(type: "integer", nullable: false),
                ActorId = table.Column<int>(type: "integer", nullable: false),
                TagId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MovieActorTags", x => new { x.MovieId, x.ActorId, x.TagId });
                table.ForeignKey(
                    name: "FK_MovieActorTags_MovieActors_MovieId_ActorId",
                    columns: x => new { x.MovieId, x.ActorId },
                    principalTable: "MovieActors",
                    principalColumns: new[] { "MovieId", "ActorId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_MovieActorTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ApexActors",
            columns: table => new
            {
                ApexId = table.Column<int>(type: "integer", nullable: false),
                ActorId = table.Column<int>(type: "integer", nullable: false),
                MovieId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ApexActors", x => new { x.ApexId, x.ActorId });
                table.ForeignKey(
                    name: "FK_ApexActors_MovieActors_MovieId_ActorId",
                    columns: x => new { x.MovieId, x.ActorId },
                    principalTable: "MovieActors",
                    principalColumns: new[] { "MovieId", "ActorId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ApexActors_MovieApexes_ApexId",
                    column: x => x.ApexId,
                    principalTable: "MovieApexes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ApexActorTags",
            columns: table => new
            {
                ApexId = table.Column<int>(type: "integer", nullable: false),
                ActorId = table.Column<int>(type: "integer", nullable: false),
                TagId = table.Column<int>(type: "integer", nullable: false),
                MovieId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ApexActorTags", x => new { x.ApexId, x.ActorId, x.TagId });
                table.ForeignKey(
                    name: "FK_ApexActorTags_MovieActors_MovieId_ActorId",
                    columns: x => new { x.MovieId, x.ActorId },
                    principalTable: "MovieActors",
                    principalColumns: new[] { "MovieId", "ActorId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ApexActorTags_MovieApexes_ApexId",
                    column: x => x.ApexId,
                    principalTable: "MovieApexes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ApexActorTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ApexEffectiveActors",
            columns: table => new
            {
                ApexId = table.Column<int>(type: "integer", nullable: false),
                ActorId = table.Column<int>(type: "integer", nullable: false),
                MovieId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ApexEffectiveActors", x => new { x.ApexId, x.ActorId });
                table.ForeignKey(
                    name: "FK_ApexEffectiveActors_MovieActors_MovieId_ActorId",
                    columns: x => new { x.MovieId, x.ActorId },
                    principalTable: "MovieActors",
                    principalColumns: new[] { "MovieId", "ActorId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ApexEffectiveActors_MovieApexes_ApexId",
                    column: x => x.ApexId,
                    principalTable: "MovieApexes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ApexEffectiveActorTags",
            columns: table => new
            {
                ApexId = table.Column<int>(type: "integer", nullable: false),
                ActorId = table.Column<int>(type: "integer", nullable: false),
                TagId = table.Column<int>(type: "integer", nullable: false),
                MovieId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ApexEffectiveActorTags", x => new { x.ApexId, x.ActorId, x.TagId });
                table.ForeignKey(
                    name: "FK_ApexEffectiveActorTags_MovieActors_MovieId_ActorId",
                    columns: x => new { x.MovieId, x.ActorId },
                    principalTable: "MovieActors",
                    principalColumns: new[] { "MovieId", "ActorId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ApexEffectiveActorTags_MovieApexes_ApexId",
                    column: x => x.ApexId,
                    principalTable: "MovieApexes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ApexEffectiveActorTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ApexTags",
            columns: table => new
            {
                ApexId = table.Column<int>(type: "integer", nullable: false),
                TagId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ApexTags", x => new { x.ApexId, x.TagId });
                table.ForeignKey(
                    name: "FK_ApexTags_MovieApexes_ApexId",
                    column: x => x.ApexId,
                    principalTable: "MovieApexes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ApexTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "HighlightActors",
            columns: table => new
            {
                HighlightId = table.Column<int>(type: "integer", nullable: false),
                ActorId = table.Column<int>(type: "integer", nullable: false),
                MovieId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HighlightActors", x => new { x.HighlightId, x.ActorId });
                table.ForeignKey(
                    name: "FK_HighlightActors_MovieActors_MovieId_ActorId",
                    columns: x => new { x.MovieId, x.ActorId },
                    principalTable: "MovieActors",
                    principalColumns: new[] { "MovieId", "ActorId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_HighlightActors_MovieHighlights_HighlightId",
                    column: x => x.HighlightId,
                    principalTable: "MovieHighlights",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "HighlightActorTags",
            columns: table => new
            {
                HighlightId = table.Column<int>(type: "integer", nullable: false),
                ActorId = table.Column<int>(type: "integer", nullable: false),
                TagId = table.Column<int>(type: "integer", nullable: false),
                MovieId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HighlightActorTags", x => new { x.HighlightId, x.ActorId, x.TagId });
                table.ForeignKey(
                    name: "FK_HighlightActorTags_MovieActors_MovieId_ActorId",
                    columns: x => new { x.MovieId, x.ActorId },
                    principalTable: "MovieActors",
                    principalColumns: new[] { "MovieId", "ActorId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_HighlightActorTags_MovieHighlights_HighlightId",
                    column: x => x.HighlightId,
                    principalTable: "MovieHighlights",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_HighlightActorTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "HighlightEffectiveActors",
            columns: table => new
            {
                HighlightId = table.Column<int>(type: "integer", nullable: false),
                ActorId = table.Column<int>(type: "integer", nullable: false),
                MovieId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HighlightEffectiveActors", x => new { x.HighlightId, x.ActorId });
                table.ForeignKey(
                    name: "FK_HighlightEffectiveActors_MovieActors_MovieId_ActorId",
                    columns: x => new { x.MovieId, x.ActorId },
                    principalTable: "MovieActors",
                    principalColumns: new[] { "MovieId", "ActorId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_HighlightEffectiveActors_MovieHighlights_HighlightId",
                    column: x => x.HighlightId,
                    principalTable: "MovieHighlights",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "HighlightEffectiveActorTags",
            columns: table => new
            {
                HighlightId = table.Column<int>(type: "integer", nullable: false),
                ActorId = table.Column<int>(type: "integer", nullable: false),
                TagId = table.Column<int>(type: "integer", nullable: false),
                MovieId = table.Column<int>(type: "integer", nullable: false),
                IsRolledUp = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HighlightEffectiveActorTags", x => new { x.HighlightId, x.ActorId, x.TagId });
                table.ForeignKey(
                    name: "FK_HighlightEffectiveActorTags_MovieActors_MovieId_ActorId",
                    columns: x => new { x.MovieId, x.ActorId },
                    principalTable: "MovieActors",
                    principalColumns: new[] { "MovieId", "ActorId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_HighlightEffectiveActorTags_MovieHighlights_HighlightId",
                    column: x => x.HighlightId,
                    principalTable: "MovieHighlights",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_HighlightEffectiveActorTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "HighlightTags",
            columns: table => new
            {
                HighlightId = table.Column<int>(type: "integer", nullable: false),
                TagId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HighlightTags", x => new { x.HighlightId, x.TagId });
                table.ForeignKey(
                    name: "FK_HighlightTags_MovieHighlights_HighlightId",
                    column: x => x.HighlightId,
                    principalTable: "MovieHighlights",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_HighlightTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SceneActors",
            columns: table => new
            {
                SceneId = table.Column<int>(type: "integer", nullable: false),
                ActorId = table.Column<int>(type: "integer", nullable: false),
                MovieId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SceneActors", x => new { x.SceneId, x.ActorId });
                table.ForeignKey(
                    name: "FK_SceneActors_MovieActors_MovieId_ActorId",
                    columns: x => new { x.MovieId, x.ActorId },
                    principalTable: "MovieActors",
                    principalColumns: new[] { "MovieId", "ActorId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SceneActors_Scenes_SceneId",
                    column: x => x.SceneId,
                    principalTable: "Scenes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SceneActorTags",
            columns: table => new
            {
                SceneId = table.Column<int>(type: "integer", nullable: false),
                ActorId = table.Column<int>(type: "integer", nullable: false),
                TagId = table.Column<int>(type: "integer", nullable: false),
                MovieId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SceneActorTags", x => new { x.SceneId, x.ActorId, x.TagId });
                table.ForeignKey(
                    name: "FK_SceneActorTags_MovieActors_MovieId_ActorId",
                    columns: x => new { x.MovieId, x.ActorId },
                    principalTable: "MovieActors",
                    principalColumns: new[] { "MovieId", "ActorId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SceneActorTags_Scenes_SceneId",
                    column: x => x.SceneId,
                    principalTable: "Scenes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SceneActorTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SceneEffectiveActors",
            columns: table => new
            {
                SceneId = table.Column<int>(type: "integer", nullable: false),
                ActorId = table.Column<int>(type: "integer", nullable: false),
                MovieId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SceneEffectiveActors", x => new { x.SceneId, x.ActorId });
                table.ForeignKey(
                    name: "FK_SceneEffectiveActors_MovieActors_MovieId_ActorId",
                    columns: x => new { x.MovieId, x.ActorId },
                    principalTable: "MovieActors",
                    principalColumns: new[] { "MovieId", "ActorId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SceneEffectiveActors_Scenes_SceneId",
                    column: x => x.SceneId,
                    principalTable: "Scenes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SceneEffectiveActorTags",
            columns: table => new
            {
                SceneId = table.Column<int>(type: "integer", nullable: false),
                ActorId = table.Column<int>(type: "integer", nullable: false),
                TagId = table.Column<int>(type: "integer", nullable: false),
                MovieId = table.Column<int>(type: "integer", nullable: false),
                IsRolledUp = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SceneEffectiveActorTags", x => new { x.SceneId, x.ActorId, x.TagId });
                table.ForeignKey(
                    name: "FK_SceneEffectiveActorTags_MovieActors_MovieId_ActorId",
                    columns: x => new { x.MovieId, x.ActorId },
                    principalTable: "MovieActors",
                    principalColumns: new[] { "MovieId", "ActorId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SceneEffectiveActorTags_Scenes_SceneId",
                    column: x => x.SceneId,
                    principalTable: "Scenes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SceneEffectiveActorTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SceneTags",
            columns: table => new
            {
                SceneId = table.Column<int>(type: "integer", nullable: false),
                TagId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SceneTags", x => new { x.SceneId, x.TagId });
                table.ForeignKey(
                    name: "FK_SceneTags_Scenes_SceneId",
                    column: x => x.SceneId,
                    principalTable: "Scenes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SceneTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.InsertData(
            table: "DeoVrGroups",
            columns: new[] { "Id", "FilterJson", "Name", "Position", "SortDescending", "SortField" },
            values: new object[] { 1, "{}", "All movies", 0, true, "added" });

        // The seed row sets Id explicitly, so the identity sequence is still at 1: advance it, or the first
        // user-added group would collide with the seed row.
        migrationBuilder.Sql("""SELECT setval(pg_get_serial_sequence('"DeoVrGroups"', 'Id'), (SELECT MAX("Id") FROM "DeoVrGroups"));""");

        migrationBuilder.CreateIndex(
            name: "IX_ActorAlbums_ActorId_Name",
            table: "ActorAlbums",
            columns: new[] { "ActorId", "Name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ActorAliases_ActorId_Name",
            table: "ActorAliases",
            columns: new[] { "ActorId", "Name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ActorCupSizePeriods_ActorId_EffectiveFrom",
            table: "ActorCupSizePeriods",
            columns: new[] { "ActorId", "EffectiveFrom" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ActorImages_ActorId_Variant",
            table: "ActorImages",
            columns: new[] { "ActorId", "Variant" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ActorPhotos_ActorId_UploadedAt",
            table: "ActorPhotos",
            columns: new[] { "ActorId", "UploadedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_ActorPhotos_AlbumId",
            table: "ActorPhotos",
            column: "AlbumId");

        migrationBuilder.CreateIndex(
            name: "IX_Actors_CupSize",
            table: "Actors",
            column: "CupSize");

        migrationBuilder.CreateIndex(
            name: "IX_Actors_FirstName_LastName",
            table: "Actors",
            columns: new[] { "FirstName", "LastName" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Actors_HeightCm",
            table: "Actors",
            column: "HeightCm");

        migrationBuilder.CreateIndex(
            name: "IX_Actors_IsFavorite",
            table: "Actors",
            column: "IsFavorite");

        migrationBuilder.CreateIndex(
            name: "IX_Actors_IsRetired",
            table: "Actors",
            column: "IsRetired");

        migrationBuilder.CreateIndex(
            name: "IX_ApexActors_MovieId_ActorId",
            table: "ApexActors",
            columns: new[] { "MovieId", "ActorId" });

        migrationBuilder.CreateIndex(
            name: "IX_ApexActorTags_MovieId_ActorId",
            table: "ApexActorTags",
            columns: new[] { "MovieId", "ActorId" });

        migrationBuilder.CreateIndex(
            name: "IX_ApexActorTags_TagId_ActorId_ApexId",
            table: "ApexActorTags",
            columns: new[] { "TagId", "ActorId", "ApexId" });

        migrationBuilder.CreateIndex(
            name: "IX_ApexEffectiveActors_ActorId_ApexId",
            table: "ApexEffectiveActors",
            columns: new[] { "ActorId", "ApexId" });

        migrationBuilder.CreateIndex(
            name: "IX_ApexEffectiveActors_MovieId_ActorId",
            table: "ApexEffectiveActors",
            columns: new[] { "MovieId", "ActorId" });

        migrationBuilder.CreateIndex(
            name: "IX_ApexEffectiveActorTags_MovieId_ActorId",
            table: "ApexEffectiveActorTags",
            columns: new[] { "MovieId", "ActorId" });

        migrationBuilder.CreateIndex(
            name: "IX_ApexEffectiveActorTags_TagId_ActorId_ApexId",
            table: "ApexEffectiveActorTags",
            columns: new[] { "TagId", "ActorId", "ApexId" });

        migrationBuilder.CreateIndex(
            name: "IX_ApexTags_TagId",
            table: "ApexTags",
            column: "TagId");

        migrationBuilder.CreateIndex(
            name: "IX_CachedImages_Code_Role_Index_Variant",
            table: "CachedImages",
            columns: new[] { "Code", "Role", "Index", "Variant" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DeletedMovies_CanonicalKey",
            table: "DeletedMovies",
            column: "CanonicalKey");

        migrationBuilder.CreateIndex(
            name: "IX_DeletedMovies_DeletedAt",
            table: "DeletedMovies",
            column: "DeletedAt");

        migrationBuilder.CreateIndex(
            name: "IX_DeletedMovies_NormalizedCode",
            table: "DeletedMovies",
            column: "NormalizedCode",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DiscoveredMovieCandidates_Code",
            table: "DiscoveredMovieCandidates",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DiscoverySourceSettings_SourceName",
            table: "DiscoverySourceSettings",
            column: "SourceName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_HighlightActors_MovieId_ActorId",
            table: "HighlightActors",
            columns: new[] { "MovieId", "ActorId" });

        migrationBuilder.CreateIndex(
            name: "IX_HighlightActorTags_MovieId_ActorId",
            table: "HighlightActorTags",
            columns: new[] { "MovieId", "ActorId" });

        migrationBuilder.CreateIndex(
            name: "IX_HighlightActorTags_TagId_ActorId_HighlightId",
            table: "HighlightActorTags",
            columns: new[] { "TagId", "ActorId", "HighlightId" });

        migrationBuilder.CreateIndex(
            name: "IX_HighlightEffectiveActors_ActorId_HighlightId",
            table: "HighlightEffectiveActors",
            columns: new[] { "ActorId", "HighlightId" });

        migrationBuilder.CreateIndex(
            name: "IX_HighlightEffectiveActors_MovieId_ActorId",
            table: "HighlightEffectiveActors",
            columns: new[] { "MovieId", "ActorId" });

        migrationBuilder.CreateIndex(
            name: "IX_HighlightEffectiveActorTags_MovieId_ActorId",
            table: "HighlightEffectiveActorTags",
            columns: new[] { "MovieId", "ActorId" });

        migrationBuilder.CreateIndex(
            name: "IX_HighlightEffectiveActorTags_TagId_ActorId_HighlightId",
            table: "HighlightEffectiveActorTags",
            columns: new[] { "TagId", "ActorId", "HighlightId" });

        migrationBuilder.CreateIndex(
            name: "IX_HighlightTags_TagId",
            table: "HighlightTags",
            column: "TagId");

        migrationBuilder.CreateIndex(
            name: "IX_IgnoredTags_Value_MatchMode",
            table: "IgnoredTags",
            columns: new[] { "Value", "MatchMode" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MovieActors_ActorId",
            table: "MovieActors",
            column: "ActorId");

        migrationBuilder.CreateIndex(
            name: "IX_MovieActorTags_TagId_ActorId_MovieId",
            table: "MovieActorTags",
            columns: new[] { "TagId", "ActorId", "MovieId" });

        migrationBuilder.CreateIndex(
            name: "IX_MovieApexes_MovieId_Seconds",
            table: "MovieApexes",
            columns: new[] { "MovieId", "Seconds" });

        migrationBuilder.CreateIndex(
            name: "IX_MovieFiles_MovieId_FileName",
            table: "MovieFiles",
            columns: new[] { "MovieId", "FileName" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MovieHighlights_MovieId_StartSeconds",
            table: "MovieHighlights",
            columns: new[] { "MovieId", "StartSeconds" });

        migrationBuilder.CreateIndex(
            name: "IX_Movies_ClipActorsStale",
            table: "Movies",
            column: "ClipActorsStale",
            filter: "\"ClipActorsStale\"");

        migrationBuilder.CreateIndex(
            name: "IX_Movies_Code",
            table: "Movies",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Movies_DateAdded",
            table: "Movies",
            column: "DateAdded");

        migrationBuilder.CreateIndex(
            name: "IX_Movies_IsFavorite",
            table: "Movies",
            column: "IsFavorite");

        migrationBuilder.CreateIndex(
            name: "IX_Movies_JellyfinLibraryName",
            table: "Movies",
            column: "JellyfinLibraryName");

        migrationBuilder.CreateIndex(
            name: "IX_Movies_NfoDriftKind",
            table: "Movies",
            column: "NfoDriftKind");

        migrationBuilder.CreateIndex(
            name: "IX_Movies_Status_DateAdded",
            table: "Movies",
            columns: new[] { "Status", "DateAdded" });

        migrationBuilder.CreateIndex(
            name: "IX_MovieTags_TagId",
            table: "MovieTags",
            column: "TagId");

        migrationBuilder.CreateIndex(
            name: "IX_NfoGenerations_MovieId_ReplacedAtUtc",
            table: "NfoGenerations",
            columns: new[] { "MovieId", "ReplacedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_PathMappings_QBittorrentPrefix",
            table: "PathMappings",
            column: "QBittorrentPrefix",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SceneActors_MovieId_ActorId",
            table: "SceneActors",
            columns: new[] { "MovieId", "ActorId" });

        migrationBuilder.CreateIndex(
            name: "IX_SceneActorTags_MovieId_ActorId",
            table: "SceneActorTags",
            columns: new[] { "MovieId", "ActorId" });

        migrationBuilder.CreateIndex(
            name: "IX_SceneActorTags_TagId_ActorId_SceneId",
            table: "SceneActorTags",
            columns: new[] { "TagId", "ActorId", "SceneId" });

        migrationBuilder.CreateIndex(
            name: "IX_SceneEffectiveActors_ActorId_SceneId",
            table: "SceneEffectiveActors",
            columns: new[] { "ActorId", "SceneId" });

        migrationBuilder.CreateIndex(
            name: "IX_SceneEffectiveActors_MovieId_ActorId",
            table: "SceneEffectiveActors",
            columns: new[] { "MovieId", "ActorId" });

        migrationBuilder.CreateIndex(
            name: "IX_SceneEffectiveActorTags_MovieId_ActorId",
            table: "SceneEffectiveActorTags",
            columns: new[] { "MovieId", "ActorId" });

        migrationBuilder.CreateIndex(
            name: "IX_SceneEffectiveActorTags_TagId_ActorId_SceneId",
            table: "SceneEffectiveActorTags",
            columns: new[] { "TagId", "ActorId", "SceneId" });

        migrationBuilder.CreateIndex(
            name: "IX_Scenes_IsFavorite",
            table: "Scenes",
            column: "IsFavorite");

        migrationBuilder.CreateIndex(
            name: "IX_Scenes_MovieId_StartSeconds",
            table: "Scenes",
            columns: new[] { "MovieId", "StartSeconds" });

        migrationBuilder.CreateIndex(
            name: "IX_SceneSuggestions_MovieId",
            table: "SceneSuggestions",
            column: "MovieId");

        migrationBuilder.CreateIndex(
            name: "IX_SceneTags_TagId",
            table: "SceneTags",
            column: "TagId");

        migrationBuilder.CreateIndex(
            name: "IX_ScheduledTaskRuns_TaskName_EndedAt",
            table: "ScheduledTaskRuns",
            columns: new[] { "TaskName", "EndedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_ScheduledTaskRuns_TaskName_QueuedAt",
            table: "ScheduledTaskRuns",
            columns: new[] { "TaskName", "QueuedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_TagReplacementRules_SourceValue_MatchMode",
            table: "TagReplacementRules",
            columns: new[] { "SourceValue", "MatchMode" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TagReplacementRules_TargetTagId",
            table: "TagReplacementRules",
            column: "TargetTagId");

        migrationBuilder.CreateIndex(
            name: "IX_Tags_Name",
            table: "Tags",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Tags_ParentTagId",
            table: "Tags",
            column: "ParentTagId");

        migrationBuilder.CreateIndex(
            name: "IX_TorrentDownloads_Hash",
            table: "TorrentDownloads",
            column: "Hash");

        migrationBuilder.CreateIndex(
            name: "IX_TorrentDownloads_MovieId",
            table: "TorrentDownloads",
            column: "MovieId");

        migrationBuilder.CreateIndex(
            name: "IX_TrickplaySets_CodeFolder_Identity",
            table: "TrickplaySets",
            columns: new[] { "CodeFolder", "Identity" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ActorAliases");

        migrationBuilder.DropTable(
            name: "ActorCupSizePeriods");

        migrationBuilder.DropTable(
            name: "ActorImages");

        migrationBuilder.DropTable(
            name: "ActorPhotos");

        migrationBuilder.DropTable(
            name: "ApexActors");

        migrationBuilder.DropTable(
            name: "ApexActorTags");

        migrationBuilder.DropTable(
            name: "ApexEffectiveActors");

        migrationBuilder.DropTable(
            name: "ApexEffectiveActorTags");

        migrationBuilder.DropTable(
            name: "ApexPlaybackSettings");

        migrationBuilder.DropTable(
            name: "ApexTags");

        migrationBuilder.DropTable(
            name: "CachedImages");

        migrationBuilder.DropTable(
            name: "DeletedMovies");

        migrationBuilder.DropTable(
            name: "DeoVrGroups");

        migrationBuilder.DropTable(
            name: "DeoVrSettings");

        migrationBuilder.DropTable(
            name: "DiscoveredMovieCandidates");

        migrationBuilder.DropTable(
            name: "DiscoverySourceSettings");

        migrationBuilder.DropTable(
            name: "HighlightActors");

        migrationBuilder.DropTable(
            name: "HighlightActorTags");

        migrationBuilder.DropTable(
            name: "HighlightEffectiveActors");

        migrationBuilder.DropTable(
            name: "HighlightEffectiveActorTags");

        migrationBuilder.DropTable(
            name: "HighlightTags");

        migrationBuilder.DropTable(
            name: "IgnoredTags");

        migrationBuilder.DropTable(
            name: "JavinizerSettings");

        migrationBuilder.DropTable(
            name: "JellyfinSettings");

        migrationBuilder.DropTable(
            name: "LocalLibrarySettings");

        migrationBuilder.DropTable(
            name: "MediaInfoSettings");

        migrationBuilder.DropTable(
            name: "MinnanoAvSettings");

        migrationBuilder.DropTable(
            name: "MovieActorTags");

        migrationBuilder.DropTable(
            name: "MovieFiles");

        migrationBuilder.DropTable(
            name: "MovieTags");

        migrationBuilder.DropTable(
            name: "NfoGenerations");

        migrationBuilder.DropTable(
            name: "PathMappings");

        migrationBuilder.DropTable(
            name: "ProwlarrSettings");

        migrationBuilder.DropTable(
            name: "QBittorrentSettings");

        migrationBuilder.DropTable(
            name: "R18DevSettings");

        migrationBuilder.DropTable(
            name: "SceneActors");

        migrationBuilder.DropTable(
            name: "SceneActorTags");

        migrationBuilder.DropTable(
            name: "SceneEffectiveActors");

        migrationBuilder.DropTable(
            name: "SceneEffectiveActorTags");

        migrationBuilder.DropTable(
            name: "SceneSuggestions");

        migrationBuilder.DropTable(
            name: "SceneTags");

        migrationBuilder.DropTable(
            name: "ScheduledTaskRuns");

        migrationBuilder.DropTable(
            name: "TagReplacementRules");

        migrationBuilder.DropTable(
            name: "TagSettings");

        migrationBuilder.DropTable(
            name: "TorrentDownloads");

        migrationBuilder.DropTable(
            name: "TrickplaySets");

        migrationBuilder.DropTable(
            name: "TrickplaySettings");

        migrationBuilder.DropTable(
            name: "WarashiSettings");

        migrationBuilder.DropTable(
            name: "ActorAlbums");

        migrationBuilder.DropTable(
            name: "MovieApexes");

        migrationBuilder.DropTable(
            name: "MovieHighlights");

        migrationBuilder.DropTable(
            name: "MovieActors");

        migrationBuilder.DropTable(
            name: "Scenes");

        migrationBuilder.DropTable(
            name: "Tags");

        migrationBuilder.DropTable(
            name: "Actors");

        migrationBuilder.DropTable(
            name: "Movies");

        migrationBuilder.AlterDatabase()
            .OldAnnotation("Npgsql:CollationDefinition:case_insensitive", "und-u-ks-level2,und-u-ks-level2,icu,False");
    }
}
