using Javbuddy.Models;
using Javbuddy.Services.QBittorrent;
using Javbuddy.Services.Torrents;

namespace Javbuddy.Tests.Services.Torrents;

public class TorrentLiveProgressTests
{
    [Fact]
    public void Apply_MatchesByHashIgnoringCase_AndCopiesProgressSpeedAndEta()
    {
        var row = new TorrentDownload { Hash = "ABCdef", Progress = 0.1, DownloadSpeedBytesPerSec = 1, EtaSeconds = 99 };

        TorrentLiveProgress.Apply([row], [new QBittorrentTorrentDto { Hash = "abcDEF", Progress = 0.6, DlSpeed = 2048, Eta = 120 }]);

        Assert.Equal(0.6, row.Progress);
        Assert.Equal(2048, row.DownloadSpeedBytesPerSec);
        Assert.Equal(120, row.EtaSeconds);
    }

    [Fact]
    public void Apply_LeavesRowsWithoutAHashOrALiveMatchAlone()
    {
        var noHash = new TorrentDownload { Progress = 0.1, DownloadSpeedBytesPerSec = 5, EtaSeconds = 7 };
        var noMatch = new TorrentDownload { Hash = "other", Progress = 0.2, DownloadSpeedBytesPerSec = 6, EtaSeconds = 8 };

        TorrentLiveProgress.Apply([noHash, noMatch], [new QBittorrentTorrentDto { Hash = "abc", Progress = 1, DlSpeed = 9, Eta = 1 }]);

        Assert.Equal(0.1, noHash.Progress);
        Assert.Equal(5, noHash.DownloadSpeedBytesPerSec);
        Assert.Equal(0.2, noMatch.Progress);
        Assert.Equal(8, noMatch.EtaSeconds);
    }

    [Fact]
    public void Apply_IgnoresLiveTorrentsWithoutAHash()
    {
        var row = new TorrentDownload { Hash = "abc", Progress = 0.1 };

        TorrentLiveProgress.Apply([row],
        [
            new QBittorrentTorrentDto { Hash = null, Progress = 0.9 },
            new QBittorrentTorrentDto { Hash = "abc", Progress = 0.3 },
        ]);

        Assert.Equal(0.3, row.Progress);
    }
}
