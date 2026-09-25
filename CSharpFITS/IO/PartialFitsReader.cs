using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;

namespace nom.tam.fits.IO;

/// <summary>
/// Memory-mapped partial FITS reader. Maps a single-HDU 2D image FITS file
/// once at construction, parses the header to cache geometry + scaling
/// parameters (<c>BITPIX</c>, <c>NAXIS1/2</c>, <c>BZERO</c>, <c>BSCALE</c>),
/// then serves arbitrary sub-rectangle reads via <see cref="ReadRegion"/> --
/// each one a single seek + bulk copy + per-pixel decode, no full-image
/// allocation.
///
/// <para>Designed for tile-pipelined integrators that iterate output canvas
/// tiles and pull just the source region under each frame's inverse
/// transform. With this reader peak RAM is bounded by the tile-column
/// working set rather than <c>N x decoded-frame</c>.</para>
///
/// <para>v1 scope: physical-axis 2D image HDU, every BITPIX; FITS is
/// big-endian on disk and we byte-swap to host order on read. Each row is
/// decoded by <see cref="BigEndianSamples"/>, the same decoder
/// <see cref="FitsReader"/> reads whole planes with, so a region and the
/// plane it came from hold the same values.</para>
///
/// <para>A mapping suits this reader's many small regions of one frame. For
/// a whole image <see cref="FitsReader"/> reads bands instead, which measured
/// faster from the page cache and from disk.</para>
///
/// <para>net10.0-only: depends on <see cref="Vector128{T}"/> and
/// <c>Unsafe.AsRef&lt;T&gt;(byte*)</c>, both of which require .NET 7+. The
/// netstandard2.0 build of FITS.Lib omits this type.</para>
/// </summary>
public sealed class PartialFitsReader : IDisposable
{
    /// <summary>
    /// The sub-rectangle of a frame to read, in 0-based pixels with the origin at the top-left.
    /// Private on purpose: it describes four integers for this reader's own internals, and the
    /// public entry point takes them as plain arguments so no caller has to name a rectangle type.
    /// </summary>
    /// <remarks>
    /// <para>This replaced an alias to the framework drawing rectangle. That type put a drawing and
    /// Windows association into the public signature purely to carry four integers, and a consumer
    /// that keeps that namespace out of its own imaging layers could not call this reader without
    /// reintroducing it.</para>
    /// <para><see cref="Right"/> and <see cref="Bottom"/> are EXCLUSIVE, exactly as before, so the
    /// bounds checks and row loops that use them keep their meaning unchanged.</para>
    /// </remarks>
    private readonly record struct PixelRegion(int X, int Y, int Width, int Height)
    {
        /// <summary>First column past the region.</summary>
        public int Right => X + Width;

        /// <summary>First row past the region.</summary>
        public int Bottom => Y + Height;

        public override string ToString() => $"[{X},{Y} {Width}x{Height}]";
    }

    /// <summary>FITS header card size in bytes.</summary>
    public const int CardSize = 80;

    /// <summary>FITS storage block size in bytes -- header + data are both
    /// padded to multiples of this.</summary>
    public const int BlockSize = 2880;

    /// <summary>Number of header cards in one block.</summary>
    public const int CardsPerBlock = BlockSize / CardSize;

    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _view;
    // Cached pointer into the mapped file; valid as long as _view is alive.
    private readonly unsafe byte* _basePtr;
    private readonly long _fileLength;

    /// <summary>Image width in pixels (<c>NAXIS1</c>).</summary>
    public int Width { get; }

    /// <summary>Image height in pixels (<c>NAXIS2</c>).</summary>
    public int Height { get; }

    /// <summary>FITS pixel-format code from the <c>BITPIX</c> card: 8 = byte,
    /// 16 = signed short, 32 = signed int, -32 = float32, -64 = float64.</summary>
    public int BitPix { get; }

    /// <summary>Bytes per pixel = <c>|BITPIX| / 8</c>.</summary>
    public int BytesPerPixel { get; }

    /// <summary>FITS scaling offset (<c>BZERO</c>; default 0). Applied as
    /// <c>physical = BZERO + BSCALE * stored</c>.</summary>
    public double BZero { get; }

    /// <summary>FITS scaling slope (<c>BSCALE</c>; default 1).</summary>
    public double BScale { get; }

    /// <summary>File byte offset where the pixel data begins (after the
    /// header's last 2880-block boundary).</summary>
    public long DataOffset { get; }

    public PartialFitsReader(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(path);
        _mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, mapName: null, capacity: 0, MemoryMappedFileAccess.Read);
        _view = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        _fileLength = _view.Capacity;

        unsafe
        {
            byte* ptr = null;
            _view.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            _basePtr = ptr;
        }

        // Parse header. Cards are 80 ASCII bytes; END card terminates; header
        // pads up to next 2880-byte boundary.
        var bitpix = 0;
        var naxis = 0;
        var width = 0;
        var height = 0;
        var bzero = 0.0;
        var bscale = 1.0;
        var ended = false;
        long offset = 0;

        while (offset + CardSize <= _fileLength && !ended)
        {
            var blockStart = offset;
            for (var c = 0; c < CardsPerBlock; c++)
            {
                var card = ReadAsciiCard(offset);
                offset += CardSize;
                if (card.StartsWith("END") && (card.Length == 3 || card[3] == ' '))
                {
                    ended = true;
                    break;
                }
                // Cards look like "KEYWORD = value / comment" with KEYWORD in 1..8
                // and "= " at columns 9..10. We only need the typed scalars.
                if (card.Length < 10 || card[8] != '=') continue;
                var keyword = card.AsSpan(0, 8).TrimEnd().ToString();
                var valueStr = ParseValue(card.AsSpan(10));
                switch (keyword)
                {
                    case "BITPIX": bitpix = ParseInt(valueStr); break;
                    case "NAXIS":  naxis  = ParseInt(valueStr); break;
                    case "NAXIS1": width  = ParseInt(valueStr); break;
                    case "NAXIS2": height = ParseInt(valueStr); break;
                    case "BZERO":  bzero  = ParseDouble(valueStr); break;
                    case "BSCALE": bscale = ParseDouble(valueStr); break;
                }
            }
            // Pad to next 2880-block start before reading the next block of cards.
            offset = blockStart + BlockSize;
        }

        if (!ended) throw new InvalidDataException($"FITS header missing END card in {path}");
        if (naxis != 2) throw new NotSupportedException($"PartialFitsReader v1 only supports NAXIS=2 images (got NAXIS={naxis} in {path})");
        if (width <= 0 || height <= 0) throw new InvalidDataException($"Bad NAXIS1/NAXIS2 in {path}: {width}x{height}");
        if (!BigEndianSamples.IsSupported(bitpix))
            throw new NotSupportedException($"PartialFitsReader doesn't support BITPIX={bitpix} (in {path})");

        Width = width;
        Height = height;
        BitPix = bitpix;
        BytesPerPixel = Math.Abs(bitpix) / 8;
        BZero = bzero;
        BScale = bscale;
        DataOffset = offset;

        var dataBytes = (long)Width * Height * BytesPerPixel;
        if (DataOffset + dataBytes > _fileLength)
        {
            throw new InvalidDataException(
                $"FITS data region extends beyond file end in {path}: offset={DataOffset} + bytes={dataBytes} > length={_fileLength}");
        }
    }

    /// <summary>
    /// Read a sub-rectangle of pixels into <paramref name="dest"/> as
    /// physical values (after applying <c>BZERO</c> + <c>BSCALE</c>).
    /// Row-major in row-major order: <c>dest[r * src.Width + c]</c>
    /// corresponds to file pixel <c>(src.X + c, src.Y + r)</c>.
    ///
    /// <para><paramref name="src"/> must lie entirely within
    /// <c>[0, Width) x [0, Height)</c>. <paramref name="dest"/> must hold at
    /// least <c>src.Width * src.Height</c> floats.</para>
    /// </summary>
    /// <summary>
    /// The same read, stated as plain edges so a caller needs no particular rectangle TYPE to ask
    /// for a region.
    /// </summary>
    /// <remarks>
    /// Added because the rectangle overload forces every caller to name a drawing type purely to
    /// describe four integers, which a consumer that keeps that namespace out of its imaging and
    /// device layers cannot do. Additive on purpose: the rectangle overload is untouched, so nothing
    /// existing changes and no release has to be coordinated to adopt this one.
    /// </remarks>
    /// <param name="x">Left edge, 0-based and inclusive.</param>
    /// <param name="y">Top edge, 0-based and inclusive.</param>
    /// <param name="width">Horizontal extent in pixels.</param>
    /// <param name="height">Vertical extent in pixels.</param>
    /// <param name="dest">Destination, holding at least <c>width * height</c> floats.</param>
    public void ReadRegion(int x, int y, int width, int height, Span<float> dest)
        => ReadRegionCore(new PixelRegion(x, y, width, height), dest);

    private void ReadRegionCore(PixelRegion src, Span<float> dest)
    {
        if (src.X < 0 || src.Y < 0 || src.Right > Width || src.Bottom > Height
            || src.Width <= 0 || src.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(src),
                $"Region {src} out of image bounds [0,0)-({Width},{Height}).");
        }
        var pixelCount = src.Width * src.Height;
        if (dest.Length < pixelCount)
        {
            throw new ArgumentException(
                $"Destination span too small: {dest.Length} < {pixelCount}.", nameof(dest));
        }

        // FITS stores 16-bit pixels as big-endian signed int16. With BZERO=32768
        // BSCALE=1 (the standard unsigned-via-signed trick) the physical range
        // is [0, 65535]; otherwise it's [-32768, 32767] scaled by BSCALE.
        var rowBytes = (long)Width * BytesPerPixel;
        var regionRowBytes = src.Width * BytesPerPixel;
        unsafe
        {
            for (var r = 0; r < src.Height; r++)
            {
                var srcRowStart = _basePtr + DataOffset + (long)(src.Y + r) * rowBytes + (long)src.X * BytesPerPixel;
                BigEndianSamples.ToSingle(BitPix, new ReadOnlySpan<byte>(srcRowStart, regionRowBytes),
                    dest.Slice(r * src.Width, src.Width), BZero, BScale);
            }
        }
    }

    private unsafe string ReadAsciiCard(long offset)
    {
        var src = _basePtr + offset;
        var srcSpan = new ReadOnlySpan<byte>(src, CardSize);
        return Encoding.ASCII.GetString(srcSpan);
    }

    private static string ParseValue(ReadOnlySpan<char> afterEquals)
    {
        // Strip the optional `/ comment`, trim whitespace. Handles quoted
        // strings ('...') by preserving the quoted region intact.
        if (afterEquals.IsEmpty) return string.Empty;
        var slash = -1;
        var inQuote = false;
        for (var i = 0; i < afterEquals.Length; i++)
        {
            var ch = afterEquals[i];
            if (ch == '\'') inQuote = !inQuote;
            else if (ch == '/' && !inQuote) { slash = i; break; }
        }
        var valuePart = slash >= 0 ? afterEquals[..slash] : afterEquals;
        return valuePart.Trim().ToString();
    }

    private static int ParseInt(string value)
        => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static double ParseDouble(string value)
        => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    public void Dispose()
    {
        unsafe
        {
            if (_basePtr is not null)
            {
                _view.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
        _view.Dispose();
        _mmf.Dispose();
    }
}
