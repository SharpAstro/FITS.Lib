#if !NETSTANDARD2_0
namespace nom.tam.fits
{
    using System;
    using System.Buffers;
    using System.Buffers.Binary;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using nom.tam.util;

    /// <summary>
    /// Writes one FITS image, forward only, onto any writable <see cref="Stream"/>: a file, a
    /// <c>GZipStream</c>, a <see cref="MemoryStream"/>. The stream is only ever written, never read,
    /// sought or wrapped.
    /// </summary>
    /// <remarks>
    /// <para><b>What it replaces for writing an image.</b> <see cref="Fits.Write(ArrayDataIO)"/> over a
    /// <see cref="BufferedFile"/> is a port of Java's data-output stack. It buffers twice (a
    /// <see cref="FileStream"/> and a <see cref="BufferedStream"/>, each handed the caller's buffer
    /// size), writes through a <see cref="BinaryWriter"/>, builds a <see cref="BinaryReader"/> over any
    /// stream it is given, so a write-only <c>GZipStream</c> is refused, and needs the whole image as
    /// one array before it writes a byte.</para>
    /// <para><b>The same bytes.</b> The header is formatted by <see cref="Header.Write(ArrayDataIO)"/>
    /// itself, into memory, so its cards, order and padding are exactly what the old path wrote; the
    /// data is the same big-endian samples padded with zeros to the same block. A file from one is a
    /// file from the other.</para>
    /// <para><b>No frame-sized buffer.</b> <see cref="Write{T}"/> swaps the caller's samples to
    /// big-endian through one rented band; <see cref="WriteInPlace{T}"/> swaps the caller's own scratch
    /// instead. A caller that produces the data a band at a time (a quantised copy of a float frame,
    /// say) therefore never holds an array the size of the image.</para>
    /// <para><b>Order is enforced.</b> <see cref="WriteHeader"/> once, then the data in as many calls
    /// as the caller likes, then <see cref="Finish"/>, which checks the data is exactly what the header
    /// declares and pads it to the 2880-byte block. Disposing without finishing closes the stream and
    /// leaves a short file behind, which is what an exception partway through a write should leave.</para>
    /// </remarks>
    public sealed class FitsWriter : IDisposable
    {
        /// <summary>The FITS logical record.</summary>
        public const int BlockSize = 2880;

        /// <summary>
        /// Bytes swapped per write by <see cref="Write{T}"/>. Large enough that a write is one system
        /// call per couple of megabytes, small enough to be pooled; a multiple of every sample size, so
        /// a band never splits a sample.
        /// </summary>
        private const int BandBytes = 2 * 1024 * 1024;

        private readonly Stream _stream;
        private readonly bool _leaveOpen;
        private int _bitpix;
        private long _expectedDataBytes = -1;
        private long _dataBytes;
        private bool _finished;
        private bool _disposed;

        /// <summary>Writes onto <paramref name="stream"/>, which only has to be writable.</summary>
        /// <param name="stream">A file, a compressing stream, a memory stream.</param>
        /// <param name="leaveOpen">Whether disposing the writer leaves the stream open.</param>
        public FitsWriter(Stream stream, bool leaveOpen = false)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (!stream.CanWrite)
            {
                throw new ArgumentException("The stream is not writable", nameof(stream));
            }

            _stream = stream;
            _leaveOpen = leaveOpen;
        }

        /// <summary>
        /// Creates <paramref name="path"/> for the image <paramref name="header"/> describes and writes
        /// the header. The file is UNBUFFERED, since every write is a whole header, a band or the
        /// padding and a buffer would only copy them, and preallocated to its final length.
        /// </summary>
        public static FitsWriter CreateFile(string path, Header header)
        {
            if (header is null)
            {
                throw new ArgumentNullException(nameof(header));
            }

            var headerBytes = FormatHeader(header);
            var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.Read,
                BufferSize = 0,
                PreallocationSize = headerBytes.Length + header.DataSize,
            });

            var writer = new FitsWriter(stream);
            try
            {
                writer.WriteFormattedHeader(header, headerBytes);
                return writer;
            }
            catch
            {
                writer.Dispose();
                throw;
            }
        }

        /// <summary>
        /// The primary header of an image with the given sample type and shape: the cards the old path
        /// writes for an array of that type and shape once <see cref="Fits.AddHDU(BasicHDU)"/> has made
        /// it the first HDU, without anyone having to hold that array.
        /// </summary>
        /// <param name="bitpix">8 (byte), 16 (short), 32 (int), 64 (long), -32 (float) or -64 (double).</param>
        /// <param name="naxis">Axis lengths in FITS order, NAXIS1 (the fastest-varying) first: an image
        /// of width W and height H is <c>(W, H)</c>, three such planes are <c>(W, H, 3)</c>.</param>
        public static Header ImageHeader(int bitpix, params int[] naxis)
        {
            ValidateBitpix(bitpix);
            if (naxis is null || naxis.Length == 0)
            {
                throw new ArgumentException("An image has at least one axis", nameof(naxis));
            }

            foreach (var length in naxis)
            {
                if (length <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(naxis), length, "Every axis length must be positive");
                }
            }

            var header = new Header();
            ImageData.FillImageHeader(header, bitpix, naxis);

            // Made PRIMARY exactly as Fits.AddHDU makes a first HDU primary: that setter drops PCOUNT and
            // GCOUNT and keeps EXTEND, and the old path writes the header only after it has run. Applied
            // through the setter itself rather than copied, so the two cannot disagree.
            var hdu = new ImageHDU(header, null);
            hdu.PrimaryHDU = true;
            return hdu.Header;
        }

        /// <summary>Data bytes written so far.</summary>
        public long DataBytesWritten => _dataBytes;

        /// <summary>Data bytes the header declares, before padding; -1 until the header is written.</summary>
        public long DataBytesExpected => _expectedDataBytes;

        /// <summary>Writes <paramref name="header"/>: its cards, END and the blank padding to the block.</summary>
        public void WriteHeader(Header header)
        {
            if (header is null)
            {
                throw new ArgumentNullException(nameof(header));
            }

            WriteFormattedHeader(header, FormatHeader(header));
        }

        /// <summary>
        /// Writes <paramref name="values"/> as big-endian samples, through a rented band, leaving the
        /// caller's span untouched. The sample type must be the one the header's BITPIX names.
        /// </summary>
        public void Write<T>(ReadOnlySpan<T> values) where T : unmanaged
        {
            BeginData<T>(values.Length);
            if (values.IsEmpty)
            {
                return;
            }

            var bytes = MemoryMarshal.AsBytes(values);
            var band = ArrayPool<byte>.Shared.Rent(Math.Min(BandBytes, bytes.Length));
            try
            {
                for (var offset = 0; offset < bytes.Length; offset += BandBytes)
                {
                    var chunk = band.AsSpan(0, Math.Min(BandBytes, bytes.Length - offset));
                    bytes.Slice(offset, chunk.Length).CopyTo(chunk);
                    ToBigEndian<T>(chunk);
                    _stream.Write(chunk);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(band);
            }

            _dataBytes += bytes.Length;
        }

        /// <summary>
        /// Writes <paramref name="values"/> as big-endian samples by swapping them IN PLACE, for a caller
        /// whose span is its own scratch: it saves the copy <see cref="Write{T}"/> makes, and the span
        /// holds big-endian samples afterwards.
        /// </summary>
        public void WriteInPlace<T>(Span<T> values) where T : unmanaged
        {
            BeginData<T>(values.Length);
            if (values.IsEmpty)
            {
                return;
            }

            var bytes = MemoryMarshal.AsBytes(values);
            ToBigEndian<T>(bytes);
            _stream.Write(bytes);
            _dataBytes += bytes.Length;
        }

        /// <summary>
        /// Pads the data with zeros to the 2880-byte block and flushes. Throws if the data written is
        /// not exactly what the header declares, since the file would otherwise be unreadable.
        /// </summary>
        public void Finish()
        {
            ThrowIfDisposed();
            if (_finished)
            {
                return;
            }

            if (_expectedDataBytes < 0)
            {
                throw new InvalidOperationException("No header has been written");
            }

            if (_dataBytes != _expectedDataBytes)
            {
                throw new InvalidOperationException(
                    $"The header declares {_expectedDataBytes} bytes of data but {_dataBytes} were written");
            }

            var pad = (int)(PaddedLength(_dataBytes) - _dataBytes);
            if (pad > 0)
            {
                Span<byte> zeros = stackalloc byte[BlockSize];
                zeros.Clear();
                _stream.Write(zeros.Slice(0, pad));
            }

            _stream.Flush();
            _finished = true;
        }

        /// <summary>Closes the stream unless the writer was told to leave it open.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (!_leaveOpen)
            {
                _stream.Dispose();
            }
        }

        private void WriteFormattedHeader(Header header, byte[] headerBytes)
        {
            ThrowIfDisposed();
            if (_expectedDataBytes >= 0)
            {
                throw new InvalidOperationException("The header has already been written");
            }

            // Read from the card: Header.Bitpix is set-only.
            var bitpix = header.GetIntValue("BITPIX");
            ValidateBitpix(bitpix);
            _stream.Write(headerBytes);
            _bitpix = bitpix;
            _expectedDataBytes = header.TrueDataSize();
        }

        private void BeginData<T>(int count) where T : unmanaged
        {
            ThrowIfDisposed();
            if (_expectedDataBytes < 0)
            {
                throw new InvalidOperationException("Write the header before the data");
            }

            if (_finished)
            {
                throw new InvalidOperationException("The image is already finished");
            }

            var bitpix = BitpixOf<T>();
            if (bitpix != _bitpix)
            {
                throw new ArgumentException($"BITPIX {_bitpix} does not store {typeof(T).Name} samples");
            }

            if (_dataBytes + ((long)count * Unsafe.SizeOf<T>()) > _expectedDataBytes)
            {
                throw new InvalidOperationException(
                    $"The header declares {_expectedDataBytes} bytes of data; this write would take it past that");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FitsWriter));
            }
        }

        private static byte[] FormatHeader(Header header)
        {
            // Header.Write itself, so the bytes are exactly the old path's: it sorts the cards into
            // their required order, adds END, and pads with blanks.
            using var memory = new MemoryStream(BlockSize);
            var output = new BufferedDataStream(memory);
            header.Write(output);
            output.Flush();
            return memory.ToArray();
        }

        private static int BitpixOf<T>() where T : unmanaged
        {
            if (typeof(T) == typeof(byte)) return 8;
            if (typeof(T) == typeof(short)) return 16;
            if (typeof(T) == typeof(int)) return 32;
            if (typeof(T) == typeof(long)) return 64;
            if (typeof(T) == typeof(float)) return -32;
            if (typeof(T) == typeof(double)) return -64;
            throw new ArgumentException($"FITS stores no {typeof(T).Name} samples: use byte, short, int, long, float or double");
        }

        private static void ValidateBitpix(int bitpix)
        {
            if (bitpix is not (8 or 16 or 32 or 64 or -32 or -64))
            {
                throw new ArgumentOutOfRangeException(nameof(bitpix), bitpix, "BITPIX must be 8, 16, 32, 64, -32 or -64");
            }
        }

        private static void ToBigEndian<T>(Span<byte> bytes) where T : unmanaged
        {
            if (!BitConverter.IsLittleEndian)
            {
                return;
            }

            switch (Unsafe.SizeOf<T>())
            {
                case 2:
                    var shorts = MemoryMarshal.Cast<byte, short>(bytes);
                    BinaryPrimitives.ReverseEndianness(shorts, shorts);
                    break;
                case 4:
                    var ints = MemoryMarshal.Cast<byte, int>(bytes);
                    BinaryPrimitives.ReverseEndianness(ints, ints);
                    break;
                case 8:
                    var longs = MemoryMarshal.Cast<byte, long>(bytes);
                    BinaryPrimitives.ReverseEndianness(longs, longs);
                    break;
            }
        }

        private static long PaddedLength(long bytes) => (bytes + BlockSize - 1) / BlockSize * BlockSize;
    }
}
#endif
