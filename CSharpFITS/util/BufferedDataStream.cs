namespace nom.tam.util
{

    /*
     * Copyright: Thomas McGlynn 1997-2007.
     * 
     * The CSharpFITS package is a C# port of Tom McGlynn's
     * nom.tam.fits Java package, initially ported by  Samuel Carliles
     *
     * Copyright: 2007 Virtual Observatory - India. 
     *
     * Use is subject to license terms
     */
    using System;
    using System.Collections;
    using System.IO;
#if !NETSTANDARD2_0
    using System.Buffers.Binary;
#endif

    /// <summary>
    /// summary description for BufferedDataStream.
    /// </summary>

    public class BufferedDataStream : RandomAccess
    {
        #region Constructors
        /// <summary>
        /// Constructor initializing stream
        /// </summary>
        /// <param name="s"></param>
        public BufferedDataStream(Stream s)
            : this(s, -1)
        {
        }
        /// <summary>
        /// Constructor initializing stream of given buffer length
        /// </summary>
        /// <param name="s"></param>
        /// <param name="bufLength"></param>
        public BufferedDataStream(Stream s, int bufLength)
        {
            _s = bufLength < 0 ? new BufferedStream(s) : new BufferedStream(s, bufLength);
            _in = new BinaryReader(_s);
            if (_s.CanWrite)
            {
                _out = new BinaryWriter(_s);
                _outBuf = new byte[0];
            }
            _garbageBuf = new byte[1024];
        }
        #endregion

        #region ArrayDataIO Members
        #region Properties
        public override bool CanRead => _s.CanRead;

        public override bool CanSeek =>
            /*
                if(_s is ConfigStream)
                {
                  return ((ConfigStream)_s).CanSeek;
                }
                */
            _s.CanSeek;

        public override bool CanWrite => _s.CanWrite;

        public override long Length => _s.Length;

        public override long Position
        {
            get => _s.Position;
            set => _s.Position = value;
        }

        protected override Stream BaseStream => _s;

        #endregion

        #region Class Variables
        protected static readonly int sbyteByteStride = 1;
        protected static readonly int boolByteStride = BitConverter.GetBytes(true).Length;
        protected static readonly int charByteStride = BitConverter.GetBytes('a').Length;
        protected static readonly int shortByteStride = BitConverter.GetBytes((short)0).Length;
        protected static readonly int intByteStride = BitConverter.GetBytes(0).Length;
        protected static readonly int longByteStride = BitConverter.GetBytes((long)0).Length;
        protected static readonly int floatByteStride = BitConverter.GetBytes(0.0f).Length;
        protected static readonly int doubleByteStride = BitConverter.GetBytes(0.0).Length;
        #endregion

        #region Read Methods
        public override bool ReadBoolean()
        {
            return _in.ReadBoolean();
        }

        //public override byte ReadByte()
        public override int ReadByte()
        {
            return _in.ReadByte();
        }

        public override sbyte ReadSByte()
        {
            return _in.ReadSByte();
        }

        public override char ReadChar()
        {
            char[] buf = new char[1];
            Read(buf);
            return buf[0];
        }

        public override short ReadInt16()
        {
            short[] buf = new short[1];
            Read(buf);
            return buf[0];
        }

        public override int ReadInt32()
        {
            int[] buf = new int[1];
            Read(buf);
            return buf[0];
        }

        public override long ReadInt64()
        {
            long[] buf = new long[1];
            Read(buf);
            return buf[0];
        }

        public override float ReadSingle()
        {
            float[] buf = new float[1];
            Read(buf);
            return buf[0];
        }

        public override double ReadDouble()
        {
            double[] buf = new double[1];
            Read(buf);
            return buf[0];
        }

        /// <summary>Read an object.  An EOF will be signaled if the
        /// object cannot be fully read.  The getPrimitiveArrayCount()
        /// method may then be used to get a minimum number of bytes read.
        /// </summary>
        /// <param name="o"> The object to be read.  This object should
        /// be a primitive (possibly multi-dimensional) array.
        /// 
        /// </param>
        /// <returns>s  The number of bytes read.
        /// 
        /// </returns>
        public override int ReadArray(Object o)
        {
            primitiveArrayCount = 0;
            return PrimitiveArrayRecurse(o);
        }

        /// <summary>Read recursively over a multi-dimensional array.</summary>
        /// <returns> The number of bytes read.</returns>
        protected int PrimitiveArrayRecurse(Object o)
        {
            if (o == null)
            {
                return primitiveArrayCount;
            }

            if (!o.GetType().IsArray)
            {
                throw new IOException($"Invalid object passed to BufferedDataStream.ReadArray:{o.GetType().FullName}");
            }

            // Is this a multidimensional array?  If so process recursively.
            //if(o.GetType().GetArrayRank() > 1 || ((Array)o).GetValue(0).GetType().IsArray)
            if (ArrayFuncs.GetDimensions(o).Length > 1)
            {
                if (ArrayFuncs.IsArrayOfArrays(o))
                {
                    IEnumerator i = ((Array)o).GetEnumerator();
                    for (bool ok = i.MoveNext(); ok; ok = i.MoveNext())
                    {
                        PrimitiveArrayRecurse(i.Current);
                    }
                }
                else
                {
                    throw new IOException("Rectangular array read not supported.");
                }
            }
            else
            {
                // This is a one-d array.  Process it using our special functions.
                Type t = o.GetType().GetElementType();
                if (typeof(bool).Equals(t))
                {
                    primitiveArrayCount += Read((bool[])o, 0, ((bool[])o).Length);
                }
                else if (typeof(byte).Equals(t))
                {
                    primitiveArrayCount += Read((byte[])o, 0, ((byte[])o).Length);
                }
                else if (typeof(sbyte).Equals(t))
                {
                    primitiveArrayCount += Read((sbyte[])o, 0, ((sbyte[])o).Length);
                }
                else if (typeof(char).Equals(t))
                {
                    primitiveArrayCount += Read((char[])o, 0, ((char[])o).Length);
                }
                else if (typeof(short).Equals(t))
                {
                    primitiveArrayCount += Read((short[])o, 0, ((short[])o).Length);
                }
                else if (typeof(int).Equals(t))
                {
                    primitiveArrayCount += Read((int[])o, 0, ((int[])o).Length);
                }
                else if (typeof(long).Equals(t))
                {
                    primitiveArrayCount += Read((long[])o, 0, ((long[])o).Length);
                }
                else if (typeof(float).Equals(t))
                {
                    primitiveArrayCount += Read((float[])o, 0, ((float[])o).Length);
                }
                else if (typeof(double).Equals(t))
                {
                    primitiveArrayCount += Read((double[])o, 0, ((double[])o).Length);
                }
                else
                {
                    throw new IOException($"Invalid object passed to BufferedDataStream.ReadArray: {o.GetType().FullName}");
                }
            }

            return primitiveArrayCount;
        }

        public override int Read(byte[] buf)
        {
            return Read(buf, 0, buf.Length);
        }

        public override int Read(sbyte[] buf)
        {
            return Read(buf, 0, buf.Length);
        }

        public override int Read(bool[] buf)
        {
            return Read(buf, 0, buf.Length);
        }

        public override int Read(short[] buf)
        {
            return Read(buf, 0, buf.Length);
        }

        public override int Read(char[] buf)
        {
            return Read(buf, 0, buf.Length);
        }

        public override int Read(int[] buf)
        {
            return Read(buf, 0, buf.Length);
        }

        public override int Read(long[] buf)
        {
            return Read(buf, 0, buf.Length);
        }

        public override int Read(float[] buf)
        {
            return Read(buf, 0, buf.Length);
        }

        public override int Read(double[] buf)
        {
            return Read(buf, 0, buf.Length);
        }

        public override int Read(byte[] buf, int offset, int size)
        {
            int result = 0;

            try
            {
                byte[] tbuf = ReadBytesExactly(size);
                Buffer.BlockCopy(tbuf, 0, buf, offset, tbuf.Length);
                result = tbuf.Length;
            }
            catch (Exception)
            {
                result = 0;
            }

            return result;
        }

        public override int Read(sbyte[] buf, int offset, int size)
        {
            int result = 0;

            try
            {
                byte[] tbuf = ReadBytesExactly(size);
                for (int i = 0; i < tbuf.Length; ++i)
                {
                    buf[i + offset] = (sbyte)tbuf[i];
                }
                result = tbuf.Length;
            }
            catch (Exception)
            {
                result = 0;
            }

            return result;
        }

        public override int Read(bool[] buf, int offset, int size)
        {
            int nRead = 0;

            try
            {
                byte[] tbuf = ReadBytesExactly(boolByteStride * size);
                for (int b = 0; b < tbuf.Length; ++nRead, b += boolByteStride)
                {
                    buf[nRead + offset] = BitConverter.ToBoolean(tbuf, b);
                }
            }
            catch (Exception)
            {
                nRead = 0;
            }

            //return nRead;

            // return no of bytes to jump for next read. hence, it is nRead times boolByteStride
            return nRead * boolByteStride;
        }

        public override int Read(char[] buf, int offset, int size)
        {
            int nRead = 0;

            try
            {
                byte[] tbuf = ReadBytesExactly(charByteStride * size);
                for (int b = 0; b < tbuf.Length; ++nRead, b += charByteStride)
                {
                    buf[nRead + offset] = (char)((tbuf[b] << 8) | tbuf[b + 1]);
                }
            }
            catch (Exception)
            {
                nRead = 0;
            }

            return nRead * charByteStride;
        }

        public override int Read(short[] buf, int offset, int size)
        {
            int nRead = 0;

            try
            {
                byte[] tbuf = ReadBytesExactly(shortByteStride * size);
#if NETSTANDARD2_0
                for (int b = 0; b < tbuf.Length; ++nRead, b += shortByteStride)
                {
                    buf[nRead + offset] = (short)((tbuf[b] << 8) | tbuf[b + 1]);
                }
#else
                for (int b = 0; b < tbuf.Length; ++nRead, b += shortByteStride)
                {
                    buf[nRead + offset] = BinaryPrimitives.ReadInt16BigEndian(tbuf.AsSpan(b));
                }
#endif
            }
            catch (Exception)
            {
                nRead = 0;
            }

            return nRead * shortByteStride;
        }

        public override int Read(int[] buf, int offset, int size)
        {
            int nRead = 0;

            try
            {
                byte[] tbuf = ReadBytesExactly(intByteStride * size);
#if NETSTANDARD2_0
                for (int b = 0; b < tbuf.Length; ++nRead, b += intByteStride)
                {
                    buf[nRead + offset] = (tbuf[b] << 24) | (tbuf[b + 1] << 16) | (tbuf[b + 2] << 8) | tbuf[b + 3];
                }
#else
                for (int b = 0; b < tbuf.Length; ++nRead, b += intByteStride)
                {
                    buf[nRead + offset] = BinaryPrimitives.ReadInt32BigEndian(tbuf.AsSpan(b));
                }
#endif
            }
            catch (Exception)
            {
                nRead = 0;
            }

            return nRead * intByteStride;
        }

        public override int Read(long[] buf, int offset, int size)
        {
            int nRead = 0;

            try
            {
                byte[] tbuf = ReadBytesExactly(longByteStride * size);
#if NETSTANDARD2_0
                for (int b = 0; b < tbuf.Length; ++nRead, b += longByteStride)
                {
                    buf[nRead + offset] = ((long)tbuf[b] << 56) | ((long)tbuf[b + 1] << 48) | ((long)tbuf[b + 2] << 40) | ((long)tbuf[b + 3] << 32)
                                        | ((long)tbuf[b + 4] << 24) | ((long)tbuf[b + 5] << 16) | ((long)tbuf[b + 6] << 8) | tbuf[b + 7];
                }
#else
                for (int b = 0; b < tbuf.Length; ++nRead, b += longByteStride)
                {
                    buf[nRead + offset] = BinaryPrimitives.ReadInt64BigEndian(tbuf.AsSpan(b));
                }
#endif
            }
            catch (Exception)
            {
                nRead = 0;
            }

            return nRead * longByteStride;
        }

        public override int Read(float[] buf, int offset, int size)
        {
            int nRead = 0;

            try
            {
                byte[] tbuf = ReadBytesExactly(floatByteStride * size);
#if NETSTANDARD2_0
                for (int b = 0; b < tbuf.Length; ++nRead, b += floatByteStride)
                {
                    int intVal = (tbuf[b] << 24) | (tbuf[b + 1] << 16) | (tbuf[b + 2] << 8) | tbuf[b + 3];
                    unsafe { buf[nRead + offset] = *(float*)&intVal; }
                }
#else
                for (int b = 0; b < tbuf.Length; ++nRead, b += floatByteStride)
                {
                    buf[nRead + offset] = BinaryPrimitives.ReadSingleBigEndian(tbuf.AsSpan(b));
                }
#endif
            }
            catch (Exception)
            {
                nRead = 0;
            }

            return nRead * floatByteStride;
        }

        public override int Read(double[] buf, int offset, int size)
        {
            int nRead = 0;

            try
            {
                byte[] tbuf = ReadBytesExactly(doubleByteStride * size);
#if NETSTANDARD2_0
                for (int b = 0; b < tbuf.Length; ++nRead, b += doubleByteStride)
                {
                    long longVal = ((long)tbuf[b] << 56) | ((long)tbuf[b + 1] << 48) | ((long)tbuf[b + 2] << 40) | ((long)tbuf[b + 3] << 32)
                                 | ((long)tbuf[b + 4] << 24) | ((long)tbuf[b + 5] << 16) | ((long)tbuf[b + 6] << 8) | tbuf[b + 7];
                    buf[nRead + offset] = BitConverter.Int64BitsToDouble(longVal);
                }
#else
                for (int b = 0; b < tbuf.Length; ++nRead, b += doubleByteStride)
                {
                    buf[nRead + offset] = BinaryPrimitives.ReadDoubleBigEndian(tbuf.AsSpan(b));
                }
#endif
            }
            catch (Exception)
            {
                nRead = 0;
            }

            return nRead * doubleByteStride;
        }

        #endregion

        #region Write Methods
        public override void Write(byte x)
        {
            _out.Write(x);
        }

        public override void Write(sbyte x)
        {
            _out.Write(x);
        }

        public override void Write(bool x)
        {
            _out.Write(x);
        }

        public override void Write(char x)
        {
            byte[] tbuf = BitConverter.GetBytes(x);
            byte bPrime = tbuf[0];
            tbuf[0] = tbuf[1];
            tbuf[1] = bPrime;

            _out.Write(tbuf);
        }

        public override void Write(short x)
        {
            byte[] tbuf = BitConverter.GetBytes(x);
            byte bPrime = tbuf[0];
            tbuf[0] = tbuf[1];
            tbuf[1] = bPrime;

            _out.Write(tbuf);
        }

        public override void Write(int x)
        {
            byte[] tbuf = BitConverter.GetBytes(x);
            byte bPrime = tbuf[0];
            tbuf[0] = tbuf[3];
            tbuf[3] = bPrime;
            bPrime = tbuf[1];
            tbuf[1] = tbuf[2];
            tbuf[2] = bPrime;

            _out.Write(tbuf);
        }

        public override void Write(long x)
        {
            byte[] tbuf = BitConverter.GetBytes(x);
            byte bPrime = tbuf[0];
            tbuf[0] = tbuf[7];
            tbuf[7] = bPrime;
            bPrime = tbuf[1];
            tbuf[1] = tbuf[6];
            tbuf[6] = bPrime;
            bPrime = tbuf[2];
            tbuf[2] = tbuf[5];
            tbuf[5] = bPrime;
            bPrime = tbuf[3];
            tbuf[3] = tbuf[4];
            tbuf[4] = bPrime;

            _out.Write(tbuf);
        }

        public override void Write(float x)
        {
            byte[] tbuf = BitConverter.GetBytes(x);
            byte bPrime = tbuf[0];
            tbuf[0] = tbuf[3];
            tbuf[3] = bPrime;
            bPrime = tbuf[1];
            tbuf[1] = tbuf[2];
            tbuf[2] = bPrime;

            _out.Write(tbuf);
        }

        public override void Write(double x)
        {
            byte[] tbuf = BitConverter.GetBytes(x);
            byte bPrime = tbuf[0];
            tbuf[0] = tbuf[7];
            tbuf[7] = bPrime;
            bPrime = tbuf[1];
            tbuf[1] = tbuf[6];
            tbuf[6] = bPrime;
            bPrime = tbuf[2];
            tbuf[2] = tbuf[5];
            tbuf[5] = bPrime;
            bPrime = tbuf[3];
            tbuf[3] = tbuf[4];
            tbuf[4] = bPrime;

            _out.Write(tbuf);
        }

        /// <summary>This routine provides efficient writing of arrays of any primitive type.
        /// The String class is also handled but it is an error to invoke this
        /// method with an object that is not an array of these types.  If the
        /// array is multidimensional, then it calls itself recursively to write
        /// the entire array.  Strings are written using the standard
        /// 1 byte format (i.e., as in writeBytes).
        /// *
        /// If the array is an array of objects, then writePrimitiveArray will
        /// be called for each element of the array.
        /// *
        /// </summary>
        /// <param name="o"> The object to be written.  It must be an array of a primitive
        /// type, Object, or String.</param>
        public override void WriteArray(Object o)
        {
            if (!o.GetType().IsArray)
            {
                throw new IOException($"Invalid object passed to BufferedDataStream.WriteArray() - {o.GetType().FullName}");
            }
            Type type = o.GetType();
            int rank = ((Array)o).Rank;

            // Is this a multidimensional array?  If so process recursively.
            if (ArrayFuncs.GetDimensions(o).Length > 1)
            //if(o.GetType().GetArrayRank() > 1 || (((Array)o).GetValue(0)).GetType().IsArray)
            {
                if (ArrayFuncs.IsArrayOfArrays(o))
                {
                    IEnumerator i = ((Array)o).GetEnumerator();
                    for (bool ok = i.MoveNext(); ok; ok = i.MoveNext())
                    {
                        WriteArray(i.Current);
                    }
                }
                else
                {
                    // Handle rectangular (multi-dimensional) arrays by flattening
                    WriteRectangularArray((Array)o);
                }
            }
            else
            {
                // This is a one-d array.  Process it using our special functions.
                Type t = o.GetType().GetElementType();

                if (typeof(bool).Equals(t))
                {
                    Write((bool[])o, 0, ((bool[])o).Length);
                }
                else if (typeof(byte).Equals(t))
                {
                    Write((byte[])o, 0, ((byte[])o).Length);
                }
                else if (typeof(sbyte).Equals(t))
                {
                    Write((sbyte[])o, 0, ((sbyte[])o).Length);
                }
                else if (typeof(char).Equals(t))
                {
                    Write((char[])o, 0, ((char[])o).Length);
                }
                else if (typeof(short).Equals(t))
                {
                    Write((short[])o, 0, ((short[])o).Length);
                }
                else if (typeof(int).Equals(t))
                {
                    Write((int[])o, 0, ((int[])o).Length);
                }
                else if (typeof(long).Equals(t))
                {
                    Write((long[])o, 0, ((long[])o).Length);
                }
                else if (typeof(float).Equals(t))
                {
                    Write((float[])o, 0, ((float[])o).Length);
                }
                else if (typeof(double).Equals(t))
                {
                    Write((double[])o, 0, ((double[])o).Length);
                }
                else if (typeof(String).Equals(t))
                {
                    Write((String[])o, 0, ((String[])o).Length);
                }
                else
                {
                    throw new IOException($"Invalid object passed to BufferedDataStream.WriteArray: {o.GetType().FullName}");
                }
            }
        }

        /// <summary>
        /// Writes a rectangular (multi-dimensional) array by iterating through all elements in row-major order.
        /// </summary>
        /// <param name="array">The rectangular array to write.</param>
        private void WriteRectangularArray(Array array)
        {
            Type elementType = array.GetType().GetElementType();
            int totalLength = array.Length;

            if (typeof(float).Equals(elementType))
            {
                float[] flat = new float[totalLength];
                Buffer.BlockCopy(array, 0, flat, 0, totalLength * sizeof(float));
                Write(flat, 0, flat.Length);
            }
            else if (typeof(double).Equals(elementType))
            {
                double[] flat = new double[totalLength];
                Buffer.BlockCopy(array, 0, flat, 0, totalLength * sizeof(double));
                Write(flat, 0, flat.Length);
            }
            else if (typeof(int).Equals(elementType))
            {
                int[] flat = new int[totalLength];
                Buffer.BlockCopy(array, 0, flat, 0, totalLength * sizeof(int));
                Write(flat, 0, flat.Length);
            }
            else if (typeof(short).Equals(elementType))
            {
                short[] flat = new short[totalLength];
                Buffer.BlockCopy(array, 0, flat, 0, totalLength * sizeof(short));
                Write(flat, 0, flat.Length);
            }
            else if (typeof(long).Equals(elementType))
            {
                long[] flat = new long[totalLength];
                Buffer.BlockCopy(array, 0, flat, 0, totalLength * sizeof(long));
                Write(flat, 0, flat.Length);
            }
            else if (typeof(byte).Equals(elementType))
            {
                byte[] flat = new byte[totalLength];
                Buffer.BlockCopy(array, 0, flat, 0, totalLength);
                Write(flat, 0, flat.Length);
            }
            else if (typeof(sbyte).Equals(elementType))
            {
                sbyte[] flat = new sbyte[totalLength];
                Buffer.BlockCopy(array, 0, flat, 0, totalLength);
                Write(flat, 0, flat.Length);
            }
            else
            {
                throw new IOException($"Rectangular array write not supported for element type: {elementType.FullName}");
            }
        }

        public override void Write(byte[] buf)
        {
            Write(buf, 0, buf.Length);
        }

        public override void Write(sbyte[] buf)
        {
            Write(buf, 0, buf.Length);
        }

        public override void Write(bool[] buf)
        {
            Write(buf, 0, buf.Length);
        }

        public override void Write(short[] buf)
        {
            Write(buf, 0, buf.Length);
        }

        public override void Write(char[] buf)
        {
            Write(buf, 0, buf.Length);
        }

        public override void Write(int[] buf)
        {
            Write(buf, 0, buf.Length);
        }

        public override void Write(long[] buf)
        {
            Write(buf, 0, buf.Length);
        }

        public override void Write(float[] buf)
        {
            Write(buf, 0, buf.Length);
        }

        public override void Write(double[] buf)
        {
            Write(buf, 0, buf.Length);
        }

        public override void Write(String[] buf)
        {
            Write(buf, 0, buf.Length);
        }

        public override void Write(byte[] buf, int offset, int size)
        {
            _out.Write(buf, offset, size);
        }

        public override void Write(sbyte[] buf, int offset, int size)
        {
            if (_outBuf.Length < size)
            {
                _outBuf = new byte[size];
            }
            for (int i = 0; i < size; ++i)
            {
                _outBuf[i] = (byte)buf[offset + i];
            }
            _out.Write(_outBuf, 0, size);
        }

        public override void Write(bool[] buf, int offset, int size)
        {
            if (_outBuf.Length < size)
            {
                _outBuf = new byte[size];
            }
            for (int i = 0; i < size; ++i)
            {
                _outBuf[i] = buf[offset + i] ? (byte)1 : (byte)0;
            }
            _out.Write(_outBuf, 0, size);
        }

        public override void Write(char[] buf, int offset, int size)
        {
            if (_outBuf.Length < charByteStride * size)
            {
                _outBuf = new byte[charByteStride * size];
            }
            for (int nWritten = 0, b = 0; nWritten < size; ++nWritten, b += charByteStride)
            {
                char val = buf[nWritten + offset];
                _outBuf[b] = (byte)(val >> 8);
                _outBuf[b + 1] = (byte)val;
            }
            _out.Write(_outBuf, 0, charByteStride * size);
        }

        public override void Write(short[] buf, int offset, int size)
        {
            if (_outBuf.Length < shortByteStride * size)
            {
                _outBuf = new byte[shortByteStride * size];
            }
            for (int nWritten = 0, b = 0; nWritten < size; ++nWritten, b += shortByteStride)
            {
                short val = buf[nWritten + offset];
                _outBuf[b] = (byte)(val >> 8);
                _outBuf[b + 1] = (byte)val;
            }
            _out.Write(_outBuf, 0, shortByteStride * size);
        }

        public override void Write(int[] buf, int offset, int size)
        {
            if (_outBuf.Length < intByteStride * size)
            {
                _outBuf = new byte[intByteStride * size];
            }
            for (int nWritten = 0, b = 0; nWritten < size; ++nWritten, b += intByteStride)
            {
                int val = buf[nWritten + offset];
                _outBuf[b] = (byte)(val >> 24);
                _outBuf[b + 1] = (byte)(val >> 16);
                _outBuf[b + 2] = (byte)(val >> 8);
                _outBuf[b + 3] = (byte)val;
            }
            _out.Write(_outBuf, 0, intByteStride * size);
        }

        public override void Write(long[] buf, int offset, int size)
        {
            if (_outBuf.Length < longByteStride * size)
            {
                _outBuf = new byte[longByteStride * size];
            }
            for (int nWritten = 0, b = 0; nWritten < size; ++nWritten, b += longByteStride)
            {
                long val = buf[nWritten + offset];
                _outBuf[b] = (byte)(val >> 56);
                _outBuf[b + 1] = (byte)(val >> 48);
                _outBuf[b + 2] = (byte)(val >> 40);
                _outBuf[b + 3] = (byte)(val >> 32);
                _outBuf[b + 4] = (byte)(val >> 24);
                _outBuf[b + 5] = (byte)(val >> 16);
                _outBuf[b + 6] = (byte)(val >> 8);
                _outBuf[b + 7] = (byte)val;
            }
            _out.Write(_outBuf, 0, longByteStride * size);
        }

#if NETSTANDARD2_0
        public override unsafe void Write(float[] buf, int offset, int size)
        {
            if (_outBuf.Length < floatByteStride * size)
            {
                _outBuf = new byte[floatByteStride * size];
            }
            for (int nWritten = 0, b = 0; nWritten < size; ++nWritten, b += floatByteStride)
            {
                float f = buf[nWritten + offset];
                int val = *(int*)&f;
                _outBuf[b] = (byte)(val >> 24);
                _outBuf[b + 1] = (byte)(val >> 16);
                _outBuf[b + 2] = (byte)(val >> 8);
                _outBuf[b + 3] = (byte)val;
            }
            _out.Write(_outBuf, 0, floatByteStride * size);
        }
#else
        public override void Write(float[] buf, int offset, int size)
        {
            if (_outBuf.Length < floatByteStride * size)
            {
                _outBuf = new byte[floatByteStride * size];
            }
            for (int nWritten = 0, b = 0; nWritten < size; ++nWritten, b += floatByteStride)
            {
                int val = BitConverter.SingleToInt32Bits(buf[nWritten + offset]);
                _outBuf[b] = (byte)(val >> 24);
                _outBuf[b + 1] = (byte)(val >> 16);
                _outBuf[b + 2] = (byte)(val >> 8);
                _outBuf[b + 3] = (byte)val;
            }
            _out.Write(_outBuf, 0, floatByteStride * size);
        }
#endif

        public override void Write(double[] buf, int offset, int size)
        {
            if (_outBuf.Length < doubleByteStride * size)
            {
                _outBuf = new byte[doubleByteStride * size];
            }
            for (int nWritten = 0, b = 0; nWritten < size; ++nWritten, b += doubleByteStride)
            {
                long val = BitConverter.DoubleToInt64Bits(buf[nWritten + offset]);
                _outBuf[b] = (byte)(val >> 56);
                _outBuf[b + 1] = (byte)(val >> 48);
                _outBuf[b + 2] = (byte)(val >> 40);
                _outBuf[b + 3] = (byte)(val >> 32);
                _outBuf[b + 4] = (byte)(val >> 24);
                _outBuf[b + 5] = (byte)(val >> 16);
                _outBuf[b + 6] = (byte)(val >> 8);
                _outBuf[b + 7] = (byte)val;
            }
            _out.Write(_outBuf, 0, doubleByteStride * size);
        }

        public override void Write(String[] buf, int offset, int size)
        {
            for (int i = offset; i < offset + size; ++i)
            {
                _out.Write(SupportClass.ToByteArray(buf[i]));
            }
        }

        #endregion

        #region Other Methods
        /*
    public override long FilePointer
    {
      get
      {
        return 0;
      }
    }
*/
        /// <summary>
        /// Flushes/Clears the output stream
        /// </summary>
        public override void Flush()
        {
            _out.Flush();
        }

        public override long Seek(long distance)
        {
            return Seek(distance, SeekOrigin.Current);
        }

        public override long Seek(long distance, SeekOrigin origin)
        {
            if (_s.CanSeek)
            {
                long oldPos = _s.Position;
                long newPos = _s.Seek(distance, origin);
                return newPos - oldPos;
            }
            else
            {
                long dist = 0;
                for (int len = _s.Read(_garbageBuf, 0, (int)Math.Min(distance, _garbageBuf.Length));
                    distance > 0 && len != -1; )
                {
                    dist += len;
                    distance -= len;
                    len = _s.Read(_garbageBuf, 0, (int)Math.Min(distance, _garbageBuf.Length));
                }
                return dist;
            }
        }

        public override void SetLength(long val)
        {
            _s.SetLength(val);
        }
        /// <summary>
        /// Closed the input/output buffer streams
        /// </summary>
        public override void Close()
        {
            _in.Close();
            _out.Close();
            _s.Close();
            _outBuf = Array.Empty<byte>();
        }
        #endregion
        #endregion

        #region Instance Variables
        protected Stream _s;
        protected BinaryReader _in;
        protected BinaryWriter _out;
        protected byte[] _outBuf;
        protected byte[] _garbageBuf;
        protected int primitiveArrayCount;
        #endregion

        #region Helper Methods
        /// <summary>
        /// Reads exactly the specified number of bytes from the underlying stream.
        /// Throws EndOfStreamException if fewer bytes are available.
        /// </summary>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>A byte array containing exactly count bytes.</returns>
        private byte[] ReadBytesExactly(int count)
        {
            byte[] buffer = new byte[count];
#if NETSTANDARD2_0
            int totalRead = 0;
            while (totalRead < count)
            {
                int bytesRead = _s.Read(buffer, totalRead, count - totalRead);
                if (bytesRead == 0)
                {
                    throw new EndOfStreamException($"Unable to read {count} bytes from stream. Only {totalRead} bytes were available.");
                }
                totalRead += bytesRead;
            }
#else
            _s.ReadExactly(buffer);
#endif
            return buffer;
        }
        #endregion
    }
}
