namespace nom.tam.fits.compression
{
    using System;

    /// <summary>
    /// Decoder for the <c>RICE_1</c> tile compression algorithm, the default of
    /// <c>fpack</c> and of every writer that follows it (Siril, SharpCap, astropy,
    /// the survey archives).
    /// <para>
    /// Rice coding splits the tile into blocks of <c>BLOCKSIZE</c> pixels, stores the
    /// first pixel verbatim and then codes each successive difference as a unary
    /// prefix plus <c>fs</c> trailing bits. This is a bit-exact port of cfitsio's
    /// <c>fits_rdecomp</c> family, including the deliberate unsigned wrap-around in
    /// the differencing, which is what makes the round trip lossless.
    /// </para>
    /// <para>
    /// The three cfitsio entry points (<c>fits_rdecomp</c>, <c>_short</c>, <c>_byte</c>)
    /// differ only in the width of the first pixel, the size of the <c>fs</c> field and
    /// the width the running value is truncated to, so they are one method here
    /// parameterised by <paramref name="bytePix"/>. Values are produced in their
    /// UNSIGNED representation; the caller casts to the signed FITS type.
    /// </para>
    /// </summary>
    internal static class RiceCodec
    {
        /// <summary>Position of the highest set bit, i.e. the number of significant
        /// bits in a byte. cfitsio ships this as a literal 256-entry table; deriving
        /// it keeps the two in step by construction.</summary>
        private static readonly byte[] NonzeroCount = BuildNonzeroCount();

        private static byte[] BuildNonzeroCount()
        {
            var t = new byte[256];
            for (int i = 1; i < 256; i++)
            {
                int n = 0;
                for (int v = i; v != 0; v >>= 1)
                {
                    n++;
                }
                t[i] = (byte)n;
            }
            return t;
        }

        /// <summary>Decode one Rice-compressed tile.</summary>
        /// <param name="src">Buffer holding the compressed tile.</param>
        /// <param name="srcOffset">Offset of the tile within <paramref name="src"/>.</param>
        /// <param name="srcLength">Length in bytes of the compressed tile.</param>
        /// <param name="dst">Destination, receiving <paramref name="count"/> values.</param>
        /// <param name="count">Number of pixels in the tile.</param>
        /// <param name="blockSize">The <c>BLOCKSIZE</c> compression parameter (normally 32).</param>
        /// <param name="bytePix">The <c>BYTEPIX</c> compression parameter: 1, 2 or 4 bytes per pixel.</param>
        internal static void Decompress(byte[] src, int srcOffset, int srcLength,
                                        int[] dst, int count, int blockSize, int bytePix)
        {
            if (count <= 0)
            {
                return;
            }
            if (blockSize <= 0)
            {
                throw new FitsException($"Invalid Rice BLOCKSIZE: {blockSize}");
            }

            int fsBits, fsMax;
            uint mask;
            switch (bytePix)
            {
                case 1: fsBits = 3; fsMax = 6; mask = 0xFFu; break;
                case 2: fsBits = 4; fsMax = 14; mask = 0xFFFFu; break;
                case 4: fsBits = 5; fsMax = 25; mask = 0xFFFFFFFFu; break;
                default: throw new FitsException($"Invalid Rice BYTEPIX: {bytePix}");
            }
            int bBits = 1 << fsBits;

            if (srcLength < bytePix + 1)
            {
                throw new FitsException(
                    $"Truncated Rice tile: {srcLength} bytes cannot hold a {bytePix}-byte first pixel");
            }

            int p = srcOffset;
            int end = srcOffset + srcLength;

            // The first pixel is stored verbatim, big-endian, in bytePix bytes.
            uint lastPix = 0;
            for (int k = 0; k < bytePix; k++)
            {
                lastPix = (lastPix << 8) | src[p++];
            }

            uint b = src[p++];  // bit buffer
            int nBits = 8;      // number of bits remaining in b

            for (int i = 0; i < count; )
            {
                // Read the fs value from the next fsBits bits.
                nBits -= fsBits;
                while (nBits < 0)
                {
                    if (p >= end) throw Truncated();
                    b = (b << 8) | src[p++];
                    nBits += 8;
                }
                int fs = (int)(b >> nBits) - 1;
                b &= (1u << nBits) - 1;

                int imax = i + blockSize;
                if (imax > count)
                {
                    imax = count;
                }

                if (fs < 0)
                {
                    // Low-entropy block: every difference is zero.
                    for (; i < imax; i++)
                    {
                        dst[i] = (int)lastPix;
                    }
                }
                else if (fs == fsMax)
                {
                    // High-entropy block: differences are stored directly, bBits wide.
                    for (; i < imax; i++)
                    {
                        int k = bBits - nBits;
                        uint diff = k >= 32 ? 0u : b << k;
                        for (k -= 8; k >= 0; k -= 8)
                        {
                            if (p >= end) throw Truncated();
                            b = src[p++];
                            diff |= b << k;
                        }
                        if (nBits > 0)
                        {
                            if (p >= end) throw Truncated();
                            b = src[p++];
                            diff |= b >> (-k);
                            b &= (1u << nBits) - 1;
                        }
                        else
                        {
                            b = 0;
                        }

                        lastPix = Undo(diff, lastPix, mask);
                        dst[i] = (int)lastPix;
                    }
                }
                else
                {
                    // Normal case: unary-coded high bits, then fs low bits.
                    for (; i < imax; i++)
                    {
                        while (b == 0)
                        {
                            if (p >= end) throw Truncated();
                            nBits += 8;
                            b = src[p++];
                        }
                        int nZero = nBits - NonzeroCount[b];
                        nBits -= nZero + 1;
                        b ^= 1u << nBits;               // flip the leading one-bit

                        nBits -= fs;
                        while (nBits < 0)
                        {
                            if (p >= end) throw Truncated();
                            b = (b << 8) | src[p++];
                            nBits += 8;
                        }
                        uint diff = ((uint)nZero << fs) | (b >> nBits);
                        b &= (1u << nBits) - 1;

                        lastPix = Undo(diff, lastPix, mask);
                        dst[i] = (int)lastPix;
                    }
                }
            }
        }

        /// <summary>Undo the zig-zag mapping of signed differences onto unsigned
        /// values, then undo the differencing. The addition is deliberately allowed
        /// to wrap: the encoder relied on it, so the decoder must too.</summary>
        private static uint Undo(uint diff, uint lastPix, uint mask)
        {
            diff = (diff & 1) == 0 ? diff >> 1 : ~(diff >> 1);
            return unchecked(diff + lastPix) & mask;
        }

        private static FitsException Truncated()
            => new FitsException("Rice decompression error: hit the end of the compressed byte stream");
    }
}
