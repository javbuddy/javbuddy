using System.Net;
using System.Net.Sockets;
using Javbuddy.Services.Images;

namespace Javbuddy.Tests.Services.Images;

public class PublicAddressGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.20.30")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:192.168.1.10")]
    [InlineData("::ffff:127.0.0.1")]
    public void IsPublic_InternalAddresses_AreBlocked(string address)
    {
        Assert.False(PublicAddressGuard.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("93.184.216.34")]
    [InlineData("2606:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    public void IsPublic_PublicAddresses_AreAllowed(string address)
    {
        Assert.True(PublicAddressGuard.IsPublic(IPAddress.Parse(address)));
    }

    [Fact]
    public async Task Download_ThroughTheGuardedHandler_RefusesALoopbackServer()
    {
        // A real listener on loopback: without the guard the request would reach it.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var client = new HttpClient(PublicAddressGuard.CreateHandler());

        var result = await RemoteImageDownloader.DownloadAsync(client, new Uri($"http://127.0.0.1:{port}/a.jpg"), 1024, CancellationToken.None);

        Assert.Equal(RemoteImageDownloadFailure.BlockedAddress, result.Failure);
        Assert.False(listener.Pending());
    }

    [Fact]
    public async Task Download_ThroughTheGuardedHandler_RefusesLocalhostByName()
    {
        using var client = new HttpClient(PublicAddressGuard.CreateHandler());

        var result = await RemoteImageDownloader.DownloadAsync(client, new Uri("http://localhost:1/a.jpg"), 1024, CancellationToken.None);

        Assert.Equal(RemoteImageDownloadFailure.BlockedAddress, result.Failure);
    }

    [Fact]
    public void BlockedAddress_HasAUserFacingMessage()
    {
        var message = RemoteImageDownloadMessages.Describe(new RemoteImageDownloadResult(RemoteImageDownloadFailure.BlockedAddress), 20);

        Assert.Equal("Importing from a local or private network address isn't allowed. Save the image and upload the file instead.", message);
    }
}
