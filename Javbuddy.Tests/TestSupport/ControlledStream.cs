namespace Javbuddy.Tests.TestSupport;

/// <summary>A non-seekable response body (so <see cref="StreamContent"/> reports no
/// Content-Length) that yields <paramref name="initialBytes"/> and then either ends, blocks until
/// the read is cancelled, or throws — for exercising chunked, mid-stream-cancelled and failing
/// downloads without a real network.</summary>
public sealed class ControlledStream(byte[] initialBytes, ControlledStream.AfterData after = ControlledStream.AfterData.End) : Stream
{
    public enum AfterData { End, BlockUntilCancelled, Throw }

    private int position;

    public static ControlledStream Blocking() => new([], AfterData.BlockUntilCancelled);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (position < initialBytes.Length)
        {
            var count = Math.Min(buffer.Length, initialBytes.Length - position);
            initialBytes.AsMemory(position, count).CopyTo(buffer);
            position += count;
            return count;
        }

        switch (after)
        {
            case AfterData.BlockUntilCancelled:
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return 0;
            case AfterData.Throw:
                throw new IOException("connection reset mid-stream");
            default:
                return 0;
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
}
