namespace nom.tam.fits.compression
{
    using System;
    using System.IO;
    using System.IO.Compression;

    /// <summary>
    /// Decoder for the <c>GZIP_1</c> and <c>GZIP_2</c> tile compression algorithms,
    /// and for the <c>GZIP_COMPRESSED_DATA</c> fallback column.
    /// <para>
    /// <c>GZIP_1</c> is a plain gzip stream over the tile's values in FITS
    /// (big-endian) byte order. <c>GZIP_2</c> gzips a byte-SHUFFLED copy instead: all
    /// the most significant bytes of the tile first, then all the second bytes, and so
    /// on. Neighbouring pixels usually share their high bytes, so grouping them lets
    /// deflate find far longer matches; it costs nothing but a transposition.
    /// </para>
    /// </summary>
    internal static class GzipCodec
    {
        /// <summary>Inflate a gzip stream.</summary>
        /// <param name="src">Buffer holding the compressed tile.</param>
        /// <param name="srcOffset">Offset of the tile within <paramref name="src"/>.</param>
        /// <param name="srcLength">Length in bytes of the compressed tile.</param>
        /// <param name="expectedLength">Uncompressed size in bytes. Used to size the
        /// destination exactly; a stream that decodes to a different length is a
        /// corrupt tile and is reported as such.</param>
        internal static byte[] Gunzip(byte[] src, int srcOffset, int srcLength, int expectedLength)
        {
            var dst = new byte[expectedLength];
            int total = 0;
            using (var input = new MemoryStream(src, srcOffset, srcLength, writable: false))
            using (var gz = new GZipStream(input, CompressionMode.Decompress))
            {
                int n;
                while (total < expectedLength
                       && (n = gz.Read(dst, total, expectedLength - total)) > 0)
                {
                    total += n;
                }

                if (total != expectedLength)
                {
                    throw new FitsException(
                        $"Gzip tile decoded to {total} bytes, expected {expectedLength}");
                }

                // A tile that keeps producing bytes past its declared size is not the
                // tile we were promised, so say so rather than silently truncating.
                if (gz.ReadByte() >= 0)
                {
                    throw new FitsException(
                        $"Gzip tile decoded to more than the expected {expectedLength} bytes");
                }
            }

            return dst;
        }

        /// <summary>Undo the <c>GZIP_2</c> byte shuffle in place of a freshly
        /// inflated buffer, returning the de-shuffled copy.</summary>
        /// <param name="buffer">The inflated, still-shuffled bytes.</param>
        /// <param name="elementSize">Size in bytes of one value: 2, 4 or 8.
        /// A size of 1 leaves the buffer untouched, since there is nothing to shuffle.</param>
        internal static byte[] Unshuffle(byte[] buffer, int elementSize)
        {
            if (elementSize <= 1)
            {
                return buffer;
            }
            if (buffer.Length % elementSize != 0)
            {
                throw new FitsException(
                    $"Shuffled tile of {buffer.Length} bytes is not a whole number of {elementSize}-byte values");
            }

            int count = buffer.Length / elementSize;
            var dst = new byte[buffer.Length];
            int src = 0;
            for (int b = 0; b < elementSize; b++)
            {
                for (int i = 0, o = b; i < count; i++, o += elementSize)
                {
                    dst[o] = buffer[src++];
                }
            }
            return dst;
        }
    }
}
