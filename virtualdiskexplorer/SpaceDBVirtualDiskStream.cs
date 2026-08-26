namespace VirtualDiskExplorer;

public class SpaceDBVirtualDiskStream : Stream
{
    private readonly Stream _partitionStream;
    private readonly long _dataOffset;
    private readonly long _length;
    private readonly List<SpaceDBBlockExtent> _extents;
    private long _position;

    public SpaceDBVirtualDiskStream(Stream partitionStream, long dataOffset, long length, List<SpaceDBBlockExtent>? extents = null)
    {
        _partitionStream = partitionStream;
        _dataOffset = dataOffset;
        _length = length;
        _extents = extents ?? new List<SpaceDBBlockExtent>();
        _position = 0;
        
        if (_extents.Count > 0)
        {
            _extents.Sort((a, b) => a.LogicalOffset.CompareTo(b.LogicalOffset));
        }
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => _position = Math.Max(0, Math.Min(value, _length));
    }

    public override void Flush() { }

    private bool TryMapOffset(long logicalPos, out long physicalPos, out long extentEnd)
    {
        physicalPos = 0;
        extentEnd = 0;
        
        if (_extents.Count == 0)
        {
            physicalPos = _dataOffset + logicalPos;
            extentEnd = _length;
            return true;
        }
        
        foreach (var extent in _extents)
        {
            if (logicalPos >= extent.LogicalOffset && logicalPos < extent.LogicalOffset + extent.Length)
            {
                physicalPos = extent.PhysicalOffset + (logicalPos - extent.LogicalOffset);
                extentEnd = extent.LogicalOffset + extent.Length;
                return true;
            }
        }
        
        return false;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || count < 0 || offset + count > buffer.Length)
            throw new ArgumentException("Invalid offset/count");
        if (_position >= _length) return 0;

        int bytesToRead = (int)Math.Min(count, _length - _position);
        int totalRead = 0;

        while (totalRead < bytesToRead)
        {
            if (TryMapOffset(_position, out long physicalPos, out long extentEnd))
            {
                long bytesInExtent = extentEnd - _position;
                int chunkSize = (int)Math.Min(bytesToRead - totalRead, bytesInExtent);
                
                if (physicalPos >= 0 && physicalPos + chunkSize <= _partitionStream.Length)
                {
                    _partitionStream.Seek(physicalPos, SeekOrigin.Begin);
                    int r = _partitionStream.Read(buffer, offset + totalRead, chunkSize);
                    if (r <= 0)
                    {
                        Array.Fill(buffer, (byte)0, offset + totalRead, chunkSize);
                        r = chunkSize;
                    }
                    totalRead += r;
                    _position += r;
                }
                else
                {
                    Array.Fill(buffer, (byte)0, offset + totalRead, chunkSize);
                    totalRead += chunkSize;
                    _position += chunkSize;
                }
            }
            else
            {
                int zeroSize = Math.Min(bytesToRead - totalRead, 4096);
                Array.Fill(buffer, (byte)0, offset + totalRead, zeroSize);
                totalRead += zeroSize;
                _position += zeroSize;
            }
        }

        return totalRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        switch (origin)
        {
            case SeekOrigin.Begin:
                _position = offset;
                break;
            case SeekOrigin.Current:
                _position += offset;
                break;
            case SeekOrigin.End:
                _position = _length + offset;
                break;
        }
        _position = Math.Max(0, Math.Min(_position, _length));
        return _position;
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
