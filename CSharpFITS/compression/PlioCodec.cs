namespace nom.tam.fits.compression
{
    using System;

    /// <summary>
    /// Decoder for the <c>PLIO_1</c> tile compression algorithm: the IRAF pixel-list
    /// format, used for masks rather than for images.
    /// <para>
    /// A tile is encoded as a "line list" of 16-bit instruction words, each carrying a
    /// 4-bit opcode in the high bits and 12 bits of data in the low bits. The list is
    /// a run-length program over the tile read in row-major order, with a running
    /// "high" value that instructions raise and lower as they go, which is why a mask
    /// of large but slowly varying values still codes to a handful of words.
    /// </para>
    /// <para>
    /// The list is preceded by a header whose length is given by word 1 (7 in every
    /// file cfitsio and IRAF write) and whose word 3 is the total length of the list,
    /// header included. Because the run lengths are 12-bit, a run longer than 4095
    /// pixels is simply emitted as several consecutive instructions.
    /// </para>
    /// </summary>
    internal static class PlioCodec
    {
        // Opcodes, as named by IRAF's plio.h.
        private const int I_ZN = 0;   // N pixels at zero
        private const int I_SH = 1;   // set the high value, 24-bit, consumes the next word
        private const int I_IH = 2;   // high += data
        private const int I_DH = 3;   // high -= data
        private const int I_HN = 4;   // N pixels at the high value
        private const int I_PN = 5;   // N pixels: N-1 at zero, then one at the high value
        private const int I_IS = 6;   // high += data, then one pixel at the high value
        private const int I_DS = 7;   // high -= data, then one pixel at the high value

        private const int DefaultHeaderLength = 7;

        /// <summary>Decode one PLIO line list into pixel values.</summary>
        /// <param name="list">The instruction words, already in host order. Values are
        /// used as unsigned 16-bit quantities.</param>
        /// <param name="dst">Destination, receiving <paramref name="count"/> values.</param>
        /// <param name="count">Number of pixels in the tile.</param>
        internal static void Decompress(int[] list, int[] dst, int count)
        {
            if (count <= 0)
            {
                return;
            }
            if (list == null || list.Length < DefaultHeaderLength)
            {
                throw new FitsException(
                    $"PLIO line list of {(list == null ? 0 : list.Length)} words is too short to hold a header");
            }

            int headerLength = list[1] & 0xFFFF;
            if (headerLength <= 0 || headerLength > list.Length)
            {
                headerLength = DefaultHeaderLength;
            }

            int listLength = list[3] & 0xFFFF;
            if (listLength <= headerLength || listLength > list.Length)
            {
                listLength = list.Length;
            }

            // IRAF's list starts with the high value implicitly at one, which is what
            // makes a plain 0/1 mask code with no value-setting instructions at all.
            int high = 1;
            int op = 0;   // output position

            for (int i = headerLength; i < listLength && op < count; i++)
            {
                int word = list[i] & 0xFFFF;
                int opcode = (word >> 12) & 0x0F;
                int data = word & 0x0FFF;

                switch (opcode)
                {
                    case I_ZN:
                        op = Fill(dst, op, count, data, 0);
                        break;

                    case I_HN:
                        op = Fill(dst, op, count, data, high);
                        break;

                    case I_PN:
                        // Zeros for all but the last pixel of the run.
                        op = Fill(dst, op, count, data - 1, 0);
                        op = Fill(dst, op, count, 1, high);
                        break;

                    case I_SH:
                        // A high value too large for 12 bits: the low half is this
                        // word's data, the high half is the next word's.
                        if (i + 1 >= listLength)
                        {
                            throw new FitsException(
                                "PLIO line list ends in the middle of a set-high-value instruction");
                        }
                        high = data | ((list[++i] & 0x0FFF) << 12);
                        break;

                    case I_IH:
                        high += data;
                        break;

                    case I_DH:
                        high -= data;
                        break;

                    case I_IS:
                        high += data;
                        op = Fill(dst, op, count, 1, high);
                        break;

                    case I_DS:
                        high -= data;
                        op = Fill(dst, op, count, 1, high);
                        break;

                    default:
                        throw new FitsException($"Unknown PLIO opcode {opcode} in line list");
                }
            }

            // A list that runs out early leaves the rest of the tile at zero, which is
            // exactly what an unwritten part of a mask means.
            for (; op < count; op++)
            {
                dst[op] = 0;
            }
        }

        private static int Fill(int[] dst, int op, int count, int n, int value)
        {
            for (int k = 0; k < n && op < count; k++)
            {
                dst[op++] = value;
            }
            return op;
        }
    }
}
