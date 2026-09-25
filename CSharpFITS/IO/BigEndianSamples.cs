using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace nom.tam.fits.IO;

/// <summary>
/// The one place big-endian FITS samples become host floats: byte-swapped, widened and scaled in a
/// single pass. <see cref="PartialFitsReader"/> decodes its regions through it and
/// <see cref="FitsReader"/> its planes, so the two can never disagree about a value.
/// </summary>
/// <remarks>
/// <para><b>Single precision, multiplied and then added.</b> For BITPIX 8, 16, 32 and -32 a sample
/// becomes <c>(float)BSCALE * stored + (float)BZERO</c>, the stored value converted to float first
/// (exactly for 8 and 16 bits, rounded to nearest for 32), and the multiply and the add are two
/// operations, never fused. That is the expression a caller converting FITS.Lib's typed arrays to
/// float writes, so a plane from here is bit for bit the plane that conversion produces. BITPIX 64 and
/// -64 are scaled in double and rounded once, since a 64-bit sample does not survive single precision
/// before the scale is applied.</para>
/// <para><b>A float image with no scaling is copied bit for bit.</b> When BSCALE and BZERO are 1 and 0
/// in single precision the swap is the whole conversion, so -0.0 stays negative and a NaN keeps its
/// payload. Multiplying by one and adding zero would turn -0.0 into +0.0.</para>
/// <para>Vector128 prologue plus scalar tail throughout: <c>Vector128.Shuffle</c> is one PSHUFB on
/// x86 (SSSE3) and one TBL on ARM NEON per swap. The tail computes the same expression as the vector
/// body, so where a sample falls relative to a vector boundary never changes its value.</para>
/// </remarks>
internal static class BigEndianSamples
{
    private static readonly Vector128<byte> Swap16Mask = Vector128.Create(
        (byte)1, 0, 3, 2, 5, 4, 7, 6, 9, 8, 11, 10, 13, 12, 15, 14);

    private static readonly Vector128<byte> Swap32Mask = Vector128.Create(
        (byte)3, 2, 1, 0, 7, 6, 5, 4, 11, 10, 9, 8, 15, 14, 13, 12);

    /// <summary>Whether BITPIX names a sample type this decodes: 8, 16, 32, 64, -32 or -64.</summary>
    public static bool IsSupported(int bitpix) => bitpix is 8 or 16 or 32 or 64 or -32 or -64;

    /// <summary>Bytes in one sample of BITPIX.</summary>
    public static int BytesPerSample(int bitpix) => Math.Abs(bitpix) / 8;

    /// <summary>
    /// Decodes <paramref name="source"/>, big-endian samples of type BITPIX, into
    /// <paramref name="destination"/> as physical values, <c>BZERO + BSCALE * stored</c>.
    /// </summary>
    /// <param name="source">Exactly <c>destination.Length * BytesPerSample(bitpix)</c> bytes.</param>
    public static void ToSingle(int bitpix, ReadOnlySpan<byte> source, Span<float> destination, double bzero, double bscale)
    {
        if (!IsSupported(bitpix))
        {
            throw new NotSupportedException($"BITPIX {bitpix} is not a FITS sample type");
        }

        if (source.Length != (long)destination.Length * BytesPerSample(bitpix))
        {
            throw new ArgumentException(
                $"{destination.Length} samples of BITPIX {bitpix} are {(long)destination.Length * BytesPerSample(bitpix)} bytes, not {source.Length}",
                nameof(source));
        }

        var zero = (float)bzero;
        var scale = (float)bscale;
        switch (bitpix)
        {
            case 8: FromUInt8(source, destination, zero, scale); break;
            case 16: FromInt16(source, destination, zero, scale); break;
            case 32: FromInt32(source, destination, zero, scale); break;
            case 64: FromInt64(source, destination, bzero, bscale); break;
            case -32 when scale == 1f && zero == 0f: SwapSingles(source, destination); break;
            case -32: FromSingle(source, destination, zero, scale); break;
            case -64: FromDouble(source, destination, bzero, bscale); break;
        }
    }

    private static void FromUInt8(ReadOnlySpan<byte> source, Span<float> destination, float zero, float scale)
    {
        // BITPIX 8 is the one unsigned type, and a byte needs no swap.
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            const int Lanes = 16;
            var zeroV = Vector128.Create(zero);
            var scaleV = Vector128.Create(scale);
            ref var src = ref MemoryMarshal.GetReference(source);
            ref var dst = ref MemoryMarshal.GetReference(destination);
            for (; i + Lanes <= destination.Length; i += Lanes)
            {
                var bytes = Vector128.LoadUnsafe(ref src, (nuint)i);
                var (low, high) = Vector128.Widen(bytes);
                var (a, b) = Vector128.Widen(low);
                var (c, d) = Vector128.Widen(high);
                (Vector128.ConvertToSingle(a.AsInt32()) * scaleV + zeroV).StoreUnsafe(ref dst, (nuint)i);
                (Vector128.ConvertToSingle(b.AsInt32()) * scaleV + zeroV).StoreUnsafe(ref dst, (nuint)(i + 4));
                (Vector128.ConvertToSingle(c.AsInt32()) * scaleV + zeroV).StoreUnsafe(ref dst, (nuint)(i + 8));
                (Vector128.ConvertToSingle(d.AsInt32()) * scaleV + zeroV).StoreUnsafe(ref dst, (nuint)(i + 12));
            }
        }

        for (; i < destination.Length; i++)
        {
            destination[i] = scale * source[i] + zero;
        }
    }

    private static void FromInt16(ReadOnlySpan<byte> source, Span<float> destination, float zero, float scale)
    {
        // Stored signed, so BZERO = 32768 with BSCALE = 1 (the standard unsigned 16-bit) maps
        // [-32768, 32767] onto [0, 65535].
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            const int Lanes = 8;
            var zeroV = Vector128.Create(zero);
            var scaleV = Vector128.Create(scale);
            ref var src = ref Unsafe.As<byte, ushort>(ref MemoryMarshal.GetReference(source));
            ref var dst = ref MemoryMarshal.GetReference(destination);
            for (; i + Lanes <= destination.Length; i += Lanes)
            {
                var bigEndian = Vector128.LoadUnsafe(ref src, (nuint)i);
                var host = Vector128.Shuffle(bigEndian.AsByte(), Swap16Mask).AsInt16();
                var (low, high) = Vector128.Widen(host);
                (Vector128.ConvertToSingle(low) * scaleV + zeroV).StoreUnsafe(ref dst, (nuint)i);
                (Vector128.ConvertToSingle(high) * scaleV + zeroV).StoreUnsafe(ref dst, (nuint)(i + 4));
            }
        }

        for (; i < destination.Length; i++)
        {
            var stored = BinaryPrimitives.ReadInt16BigEndian(source.Slice(i * 2, 2));
            destination[i] = scale * stored + zero;
        }
    }

    private static void FromInt32(ReadOnlySpan<byte> source, Span<float> destination, float zero, float scale)
    {
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            const int Lanes = 4;
            var zeroV = Vector128.Create(zero);
            var scaleV = Vector128.Create(scale);
            ref var src = ref Unsafe.As<byte, uint>(ref MemoryMarshal.GetReference(source));
            ref var dst = ref MemoryMarshal.GetReference(destination);
            for (; i + Lanes <= destination.Length; i += Lanes)
            {
                var bigEndian = Vector128.LoadUnsafe(ref src, (nuint)i);
                var host = Vector128.Shuffle(bigEndian.AsByte(), Swap32Mask).AsInt32();
                (Vector128.ConvertToSingle(host) * scaleV + zeroV).StoreUnsafe(ref dst, (nuint)i);
            }
        }

        for (; i < destination.Length; i++)
        {
            var stored = BinaryPrimitives.ReadInt32BigEndian(source.Slice(i * 4, 4));
            destination[i] = scale * stored + zero;
        }
    }

    private static void SwapSingles(ReadOnlySpan<byte> source, Span<float> destination)
        => BinaryPrimitives.ReverseEndianness(MemoryMarshal.Cast<byte, int>(source), MemoryMarshal.Cast<float, int>(destination));

    private static void FromSingle(ReadOnlySpan<byte> source, Span<float> destination, float zero, float scale)
    {
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            const int Lanes = 4;
            var zeroV = Vector128.Create(zero);
            var scaleV = Vector128.Create(scale);
            ref var src = ref Unsafe.As<byte, uint>(ref MemoryMarshal.GetReference(source));
            ref var dst = ref MemoryMarshal.GetReference(destination);
            for (; i + Lanes <= destination.Length; i += Lanes)
            {
                var bigEndian = Vector128.LoadUnsafe(ref src, (nuint)i);
                // Float bits take the same 4-byte swap as ints, then are reinterpreted.
                var host = Vector128.Shuffle(bigEndian.AsByte(), Swap32Mask).AsSingle();
                (host * scaleV + zeroV).StoreUnsafe(ref dst, (nuint)i);
            }
        }

        for (; i < destination.Length; i++)
        {
            var stored = BinaryPrimitives.ReadSingleBigEndian(source.Slice(i * 4, 4));
            destination[i] = scale * stored + zero;
        }
    }

    private static void FromInt64(ReadOnlySpan<byte> source, Span<float> destination, double zero, double scale)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            var stored = BinaryPrimitives.ReadInt64BigEndian(source.Slice(i * 8, 8));
            destination[i] = (float)(zero + scale * stored);
        }
    }

    private static void FromDouble(ReadOnlySpan<byte> source, Span<float> destination, double zero, double scale)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            var stored = BinaryPrimitives.ReadDoubleBigEndian(source.Slice(i * 8, 8));
            destination[i] = (float)(zero + scale * stored);
        }
    }
}
