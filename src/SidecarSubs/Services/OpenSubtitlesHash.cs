using System.Buffers.Binary;

namespace SidecarSubs.Services;

public static class OpenSubtitlesHash
{
    private const int ChunkSize = 64 * 1024;

    public static async Task<string> ComputeAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: ChunkSize,
            useAsync: true);

        if (stream.Length < ChunkSize * 2L)
            throw new InvalidOperationException("File is too small for an OpenSubtitles movie hash.");

        ulong hash = unchecked((ulong)stream.Length);
        var buffer = new byte[ChunkSize];

        stream.Seek(0, SeekOrigin.Begin);
        var firstRead = await ReadChunkAsync(stream, buffer, cancellationToken);
        hash = unchecked(hash + SumUInt64LittleEndian(buffer.AsSpan(0, firstRead)));

        stream.Seek(-ChunkSize, SeekOrigin.End);
        var lastRead = await ReadChunkAsync(stream, buffer, cancellationToken);
        hash = unchecked(hash + SumUInt64LittleEndian(buffer.AsSpan(0, lastRead)));

        return hash.ToString("x16");
    }

    private static async Task<int> ReadChunkAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(total, buffer.Length - total),
                cancellationToken);

            if (read == 0)
                break;

            total += read;
        }

        return total;
    }

    private static ulong SumUInt64LittleEndian(ReadOnlySpan<byte> data)
    {
        ulong sum = 0;
        var length = data.Length - (data.Length % sizeof(ulong));

        for (var offset = 0; offset < length; offset += sizeof(ulong))
            sum = unchecked(sum + BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, sizeof(ulong))));

        return sum;
    }
}
