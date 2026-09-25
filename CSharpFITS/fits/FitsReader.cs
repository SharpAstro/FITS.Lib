#if !NETSTANDARD2_0
#nullable enable
namespace nom.tam.fits
{
    using System;
    using System.Buffers;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using Microsoft.Win32.SafeHandles;
    using nom.tam.fits.IO;
    using nom.tam.util;

    /// <summary>
    /// Reads one FITS image, the first image HDU of a file, from the file straight into the caller's
    /// planes: the reading half of <see cref="FitsWriter"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>What it replaces for reading an image.</b> <see cref="Fits.ReadHDU"/> over a
    /// <see cref="BufferedFile"/> reads the whole data block into a typed array the size of the image,
    /// through a read-ahead buffer the caller sizes, before a caller that wants floats can convert one
    /// sample, so that caller holds the typed array and its own plane at once. This reads a plane a
    /// band at a time and swaps, widens and scales each band into the caller's span: the only buffer
    /// is one rented band.</para>
    /// <para><b>Positional reads, not a mapping.</b> Measured on a 26 MP BITPIX 16 image on win-arm64
    /// (2026-09-25): 10 ms from the page cache, where a mapping of the same file took 25 ms because it
    /// pays a page fault for every page on every read; and 66 ms from disk, against 106 ms for the
    /// mapping, which faults the file in a few pages at a time where a 2 MB read is one request.
    /// Either way the bytes come out of the page cache every process shares.
    /// <see cref="PartialFitsReader"/> keeps its mapping, which suits many small regions of one
    /// frame.</para>
    /// <para><b>The image the old path reads.</b> It walks the file as <see cref="Fits.ReadHDU"/>
    /// would, to the first HDU that <see cref="ImageHDU.IsHeader(Header)"/> accepts and that holds at
    /// least one sample (NAXIS above zero, no axis of length zero, which passes over an empty primary
    /// and over the placeholder FITS.Lib writes in front of a table), skipping every HDU before it by
    /// the data size its header declares, and parses each header with
    /// <see cref="Header.ReadHeader(ArrayDataIO)"/> itself, so <see cref="Hdu"/> carries exactly the
    /// cards the old path gives. A tile-compressed image cannot be read a band at a time, so a file
    /// whose first image is one (<see cref="CompressedImageHDU.IsHeader(Header)"/>, while
    /// <see cref="FitsFactory.UseTileCompression"/> is on) is declined rather than skipped: skipping it
    /// would read a later image than the old path does.</para>
    /// <para><b>Declining is not an error.</b> <see cref="TryOpen"/> answers false for a file it cannot
    /// read this way: not FITS, no image HDU, a tile-compressed image, random groups or a sample type it
    /// does not take, an image with no pixels or a plane too large for one span, data cut short. A
    /// caller that wants any image falls back to <see cref="Fits"/> on a result rather than on an
    /// exception. A failure to open or read the file still throws.</para>
    /// <para><see cref="ReadPlane"/> may be called from several threads at once: each call reads at its
    /// own offsets and rents its own band.</para>
    /// </remarks>
    public sealed class FitsReader : IDisposable
    {
        /// <summary>The FITS logical record.</summary>
        public const int BlockSize = 2880;

        private const int CardSize = 80;

        /// <summary>
        /// Bytes read and decoded per step of <see cref="ReadPlane"/>: one system call per couple of
        /// megabytes, and a multiple of every sample size, so a band never splits a sample.
        /// </summary>
        private const int BandBytes = 2 * 1024 * 1024;

        /// <summary>
        /// A header longer than this without an END card is not read as FITS. Real headers are a few
        /// blocks; this only stops a file that merely starts like one from being scanned to its end.
        /// </summary>
        private const int MaxHeaderBytes = 1024 * 1024;

        private readonly SafeFileHandle _file;
        private readonly long _planeBytes;
        private readonly double _bzero;
        private readonly double _bscale;
        private bool _disposed;

        private FitsReader(SafeFileHandle file, BasicHDU hdu, int bitpix, int width, int height, int planes, long dataOffset)
        {
            _file = file;
            Hdu = hdu;
            BitPix = bitpix;
            Width = width;
            Height = height;
            Planes = planes;
            DataOffset = dataOffset;
            _planeBytes = (long)width * height * BigEndianSamples.BytesPerSample(bitpix);
            _bzero = hdu.BZero;
            _bscale = hdu.BScale;
        }

        /// <summary>
        /// Opens <paramref name="path"/> and finds its first image HDU, or answers false when the file
        /// cannot be read this way (see the remarks), leaving <paramref name="reader"/> null.
        /// </summary>
        /// <exception cref="IOException">The file could not be opened or read.</exception>
        public static bool TryOpen(string path, [NotNullWhen(true)] out FitsReader? reader)
        {
            reader = null;
            var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);
            try
            {
                reader = FindImage(file);
                return reader is not null;
            }
            finally
            {
                if (reader is null)
                {
                    file.Dispose();
                }
            }
        }

        /// <summary>
        /// The image HDU as <see cref="Fits.ReadHDUHeaderOnly"/> returns it: its full header, and a data
        /// object holding no pixels.
        /// </summary>
        public BasicHDU Hdu { get; }

        /// <summary>The image HDU's header.</summary>
        public Header Header => Hdu.Header;

        /// <summary>The sample type: 8, 16, 32, 64, -32 or -64.</summary>
        public int BitPix { get; }

        /// <summary>Samples per row, NAXIS1.</summary>
        public int Width { get; }

        /// <summary>Rows per plane, NAXIS2, or 1 for a one-dimensional image.</summary>
        public int Height { get; }

        /// <summary>Planes: the product of NAXIS3 and every later axis, or 1 for an image of two axes
        /// or fewer.</summary>
        public int Planes { get; }

        /// <summary>BZERO, 0 when absent.</summary>
        public double BZero => _bzero;

        /// <summary>BSCALE, 1 when absent.</summary>
        public double BScale => _bscale;

        /// <summary>Where the image's data starts in the file, on a block boundary.</summary>
        public long DataOffset { get; }

        /// <summary>
        /// Reads plane <paramref name="plane"/> into <paramref name="destination"/> as physical values,
        /// <c>BZERO + BSCALE * stored</c>, row after row (NAXIS1 fastest).
        /// </summary>
        /// <remarks>
        /// <para>For BITPIX 8, 16, 32 and -32 in single precision: <c>(float)BSCALE * stored +
        /// (float)BZERO</c>, multiplied and then added, which is exactly what converting the typed
        /// array <see cref="Fits.ReadHDU"/> reads gives. A float image whose BSCALE and BZERO are 1 and 0
        /// is copied bit for bit. BITPIX 64 and -64 are scaled in double and rounded once.</para>
        /// </remarks>
        /// <param name="plane">0 to <see cref="Planes"/> - 1.</param>
        /// <param name="destination">Exactly <see cref="Width"/> * <see cref="Height"/> samples.</param>
        public void ReadPlane(int plane, Span<float> destination)
        {
            ThrowIfDisposed();
            if ((uint)plane >= (uint)Planes)
            {
                throw new ArgumentOutOfRangeException(nameof(plane), plane, $"The image has {Planes} planes");
            }

            var samples = Width * Height;
            if (destination.Length != samples)
            {
                throw new ArgumentException($"A plane is {samples} samples; the destination holds {destination.Length}", nameof(destination));
            }

            var bytesPerSample = BigEndianSamples.BytesPerSample(BitPix);
            var bandBytes = (int)Math.Min(BandBytes, _planeBytes);
            var bandSamples = bandBytes / bytesPerSample;
            var start = DataOffset + (plane * _planeBytes);
            var band = ArrayPool<byte>.Shared.Rent(bandBytes);
            try
            {
                for (var done = 0; done < samples; done += bandSamples)
                {
                    var count = Math.Min(bandSamples, samples - done);
                    var bytes = band.AsSpan(0, count * bytesPerSample);
                    ReadExactly(bytes, start + ((long)done * bytesPerSample));
                    BigEndianSamples.ToSingle(BitPix, bytes, destination.Slice(done, count), _bzero, _bscale);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(band);
            }
        }

        /// <summary>Closes the file.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _file.Dispose();
        }

        /// <summary>
        /// The walk: each HDU's header is found by its END card, parsed by <see cref="Header"/> itself,
        /// then either read (the first plain image), declined (a tile-compressed image first) or
        /// skipped by its data size.
        /// </summary>
        private static FitsReader? FindImage(SafeFileHandle file)
        {
            var length = System.IO.RandomAccess.GetLength(file);
            var buffer = ArrayPool<byte>.Shared.Rent(BlockSize);
            try
            {
                long position = 0;
                while (position < length)
                {
                    if (!TryReadHeaderBlocks(file, position, length, ref buffer, out var headerBytes))
                    {
                        return null;
                    }

                    // A copy, not the rented buffer: a header read from a seekable stream keeps that
                    // stream (for Rewrite), and the pool would hand the buffer to someone else.
                    var cards = new HeaderBlocks(buffer.AsSpan(0, headerBytes).ToArray(), position);
                    var header = Header.ReadHeader(new BufferedDataStream(cards, headerBytes));
                    if (header is null || !header.ValidHeader)
                    {
                        return null;
                    }

                    var dataOffset = position + headerBytes;
                    if (FitsFactory.UseTileCompression && CompressedImageHDU.IsHeader(header))
                    {
                        return null;
                    }

                    if (ImageHDU.IsHeader(header) && HoldsSamples(header))
                    {
                        return Describe(file, header, dataOffset, length);
                    }

                    if (DataBytes(header) is not { } dataBytes)
                    {
                        return null;
                    }

                    position = dataOffset + Padded(dataBytes);
                }

                return null;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>The reader for the image HDU <paramref name="header"/> describes, or null for a
        /// layout this does not read.</summary>
        private static FitsReader? Describe(SafeFileHandle file, Header header, long dataOffset, long fileLength)
        {
            var bitpix = header.GetIntValue("BITPIX", 0);
            var naxis = header.GetIntValue("NAXIS", 0);
            if (!BigEndianSamples.IsSupported(bitpix) || header.GetIntValue("GCOUNT", 1) != 1 || header.GetIntValue("PCOUNT", 0) != 0)
            {
                return null;
            }

            var width = header.GetIntValue("NAXIS1", 0);
            var height = naxis >= 2 ? header.GetIntValue("NAXIS2", 0) : 1;
            long planes = 1;
            for (var axis = 3; axis <= naxis; axis++)
            {
                planes *= header.GetIntValue($"NAXIS{axis}", 0);
            }

            if (width <= 0 || height <= 0 || planes <= 0 || planes > int.MaxValue || (long)width * height > int.MaxValue)
            {
                return null;
            }

            var dataBytes = (long)width * height * planes * BigEndianSamples.BytesPerSample(bitpix);
            if (dataOffset + dataBytes > fileLength)
            {
                return null;
            }

            var hdu = FitsFactory.HDUFactory(header, header.MakeData());
            return new FitsReader(file, hdu, bitpix, width, height, (int)planes, dataOffset);
        }

        /// <summary>
        /// Reads the blocks of the header starting at <paramref name="position"/> into
        /// <paramref name="buffer"/>, growing it, up to and including the block holding END. False when
        /// the bytes there are not a FITS header as <see cref="Header.Read"/> takes one: the first card
        /// neither SIMPLE nor XTENSION, a card of zeros (the old reader's end of file), or no END before
        /// the end of the file or <see cref="MaxHeaderBytes"/>.
        /// </summary>
        private static bool TryReadHeaderBlocks(SafeFileHandle file, long position, long fileLength, ref byte[] buffer, out int headerBytes)
        {
            headerBytes = 0;
            for (var read = 0; ; read += BlockSize)
            {
                if (position + read + BlockSize > fileLength || read + BlockSize > MaxHeaderBytes)
                {
                    return false;
                }

                if (buffer.Length < read + BlockSize)
                {
                    var grown = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    buffer.AsSpan(0, read).CopyTo(grown);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = grown;
                }

                var block = buffer.AsSpan(read, BlockSize);
                ReadExactly(file, block, position + read);
                for (var card = 0; card < BlockSize / CardSize; card++)
                {
                    var image = block.Slice(card * CardSize, CardSize);
                    if (image.IndexOfAnyExcept((byte)0) < 0)
                    {
                        return false;
                    }

                    if (read == 0 && card == 0 && !image.StartsWith("SIMPLE  "u8) && !image.StartsWith("XTENSION"u8))
                    {
                        return false;
                    }

                    if (image.StartsWith("END     "u8))
                    {
                        headerBytes = read + BlockSize;
                        return true;
                    }
                }
            }
        }

        /// <summary>
        /// The unpadded data size <paramref name="header"/> declares, by the standard's formula and in
        /// 64 bits: <c>|BITPIX| / 8 * GCOUNT * (PCOUNT + NAXIS1 * ... * NAXISn)</c>, NAXIS1 left out of
        /// the product for random groups. Null for a header that declares no sensible size.
        /// </summary>
        private static long? DataBytes(Header header)
        {
            var naxis = header.GetIntValue("NAXIS", 0);
            if (naxis == 0)
            {
                return 0;
            }

            var firstAxis = header.GetBooleanValue("GROUPS", false) && naxis > 1 && header.GetIntValue("NAXIS1", 0) == 0 ? 2 : 1;
            long size = 1;
            for (var axis = firstAxis; axis <= naxis; axis++)
            {
                var length = header.GetIntValue($"NAXIS{axis}", 0);
                if (length < 0)
                {
                    return null;
                }

                size *= length;
            }

            var pcount = header.GetIntValue("PCOUNT", 0);
            var gcount = header.GetIntValue("GCOUNT", 1);
            if (pcount < 0 || gcount < 0)
            {
                return null;
            }

            return (size + pcount) * gcount * BigEndianSamples.BytesPerSample(header.GetIntValue("BITPIX", 0));
        }

        /// <summary>
        /// Whether an image header describes at least one sample: NAXIS above zero and no axis of
        /// length zero. The placeholder primary FITS.Lib writes in front of a table
        /// (<see cref="BasicHDU.DummyHDU"/>, the image of an empty array: NAXIS = 1, NAXIS1 = 0) is an
        /// image HDU that holds nothing, and reading "the first image" as that one would miss the image
        /// the file carries.
        /// </summary>
        private static bool HoldsSamples(Header header)
        {
            var naxis = header.GetIntValue("NAXIS", 0);
            if (naxis <= 0)
            {
                return false;
            }

            for (var axis = 1; axis <= naxis; axis++)
            {
                if (header.GetIntValue($"NAXIS{axis}", 0) <= 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static long Padded(long bytes) => (bytes + BlockSize - 1) / BlockSize * BlockSize;

        private void ReadExactly(Span<byte> destination, long offset) => ReadExactly(_file, destination, offset);

        private static void ReadExactly(SafeFileHandle file, Span<byte> destination, long offset)
        {
            while (!destination.IsEmpty)
            {
                var read = System.IO.RandomAccess.Read(file, destination, offset);
                if (read == 0)
                {
                    throw new EndOfStreamException($"The file ended at {offset}, inside data its header declares");
                }

                destination = destination.Slice(read);
                offset += read;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FitsReader));
            }
        }

        /// <summary>
        /// A header's blocks, read-only, at the position they occupy in the file. A header records
        /// where it was read from (<see cref="Header.FileOffset"/>, and so
        /// <see cref="BasicHDU.FileOffset"/>) as its stream's position when parsing starts, so a plain
        /// memory stream would report 0 where the old path reports the header's real offset.
        /// </summary>
        private sealed class HeaderBlocks(byte[] bytes, long fileOffset) : Stream
        {
            private long _position;

            public override bool CanRead => true;

            public override bool CanSeek => true;

            public override bool CanWrite => false;

            public override long Length => fileOffset + bytes.Length;

            public override long Position
            {
                get => fileOffset + _position;
                set => _position = Clamp(value - fileOffset);
            }

            public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

            public override int Read(Span<byte> destination)
            {
                var available = bytes.AsSpan((int)_position);
                var count = Math.Min(available.Length, destination.Length);
                available.Slice(0, count).CopyTo(destination);
                _position += count;
                return count;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                _position = origin switch
                {
                    SeekOrigin.Begin => Clamp(offset - fileOffset),
                    SeekOrigin.Current => Clamp(_position + offset),
                    _ => Clamp(bytes.Length + offset),
                };
                return Position;
            }

            public override void Flush()
            {
            }

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            private long Clamp(long position) => Math.Clamp(position, 0, bytes.Length);
        }
    }
}
#endif
