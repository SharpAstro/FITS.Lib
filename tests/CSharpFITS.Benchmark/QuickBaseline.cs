using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using nom.tam.fits;
using nom.tam.util;

namespace CSharpFITS.Benchmark;

/// <summary>
/// Quick baseline measurement (not BenchmarkDotNet) for fast iteration.
/// Run with: dotnet run -c Release -- quick
/// </summary>
public static class QuickBaseline
{
    public static void Run()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "CSharpFITS.sln")))
            dir = Path.GetDirectoryName(dir);

        var fitsFilePath = Path.Combine(dir!, "tests", "CSharpFITS.Test", "testdocs", "LDN1089_singleFrame.fits");
        var fitsWritePath = Path.Combine(Path.GetTempPath(), "fits_write_bench.fits");

        Console.WriteLine($"File: {fitsFilePath}");
        Console.WriteLine($"Size: {new FileInfo(fitsFilePath).Length / (1024.0 * 1024.0):F1} MB");
        Console.WriteLine();

        // Warmup read
        object? warmupData;
        {
            var fits = new Fits(fitsFilePath);
            var hdus = fits.Read();
            warmupData = ((ImageHDU)hdus[0]).Data.DataArray;
            fits.Close();
        }

        // Benchmark: header-only (deferred)
        var sw = Stopwatch.StartNew();
        const int headerRuns = 5;
        for (int i = 0; i < headerRuns; i++)
        {
            var fits = new Fits(fitsFilePath);
            fits.Read();
            fits.Close();
        }
        sw.Stop();
        Console.WriteLine($"Header-only (deferred): {sw.ElapsedMilliseconds / headerRuns:F1} ms avg ({headerRuns} runs)");

        // Benchmark: full data load
        const int dataRuns = 5;
        string? hash = null;
        object? lastData = null;
        sw.Restart();
        for (int i = 0; i < dataRuns; i++)
        {
            var fits = new Fits(fitsFilePath);
            var hdus = fits.Read();
            foreach (var hdu in hdus)
            {
                if (hdu is ImageHDU imageHdu)
                {
                    lastData = imageHdu.Data.DataArray;
                }
            }

            if (i == dataRuns - 1 && lastData != null)
            {
                hash = ComputeImageHash(lastData);
            }

            fits.Close();
        }
        sw.Stop();
        Console.WriteLine($"Full data load:        {sw.ElapsedMilliseconds / dataRuns:F1} ms avg ({dataRuns} runs)");
        Console.WriteLine($"Image data SHA256:     {hash}");
        Console.WriteLine();

        // Benchmark: write (round-trip the loaded data)
        // Build a Fits object with the image data from the last read
        const int writeRuns = 3;

        // Warmup write
        {
            var f = new Fits();
            f.AddHDU(Fits.MakeHDU(lastData));
            var bf = new BufferedFile(fitsWritePath, FileAccess.ReadWrite, FileShare.ReadWrite);
            f.Write(bf);
            bf.Close();
        }

        sw.Restart();
        for (int i = 0; i < writeRuns; i++)
        {
            var f = new Fits();
            f.AddHDU(Fits.MakeHDU(lastData));
            var bf = new BufferedFile(fitsWritePath, FileAccess.ReadWrite, FileShare.ReadWrite);
            f.Write(bf);
            bf.Close();
        }
        sw.Stop();
        Console.WriteLine($"Full data write:       {sw.ElapsedMilliseconds / writeRuns:F1} ms avg ({writeRuns} runs)");

        // Verify round-trip: read back and hash
        {
            var fits = new Fits(fitsWritePath);
            var hdus = fits.Read();
            var rtData = ((ImageHDU)hdus[0]).Data.DataArray;
            var rtHash = ComputeImageHash(rtData);
            fits.Close();
            Console.WriteLine($"Round-trip SHA256:     {rtHash}");
            Console.WriteLine($"Round-trip matches:    {rtHash == hash}");
        }

        try { File.Delete(fitsWritePath); } catch { }
    }

    public static string ComputeImageHash(object data)
    {
        using var sha256 = SHA256.Create();
        HashArrayRecursive(sha256, data);
        sha256.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha256.Hash!);
    }

    private static void HashArrayRecursive(SHA256 sha256, object o)
    {
        if (o is float[] floatArr)
        {
            var bytes = MemoryMarshal.AsBytes(floatArr.AsSpan());
            sha256.TransformBlock(bytes.ToArray(), 0, bytes.Length, null, 0);
        }
        else if (o is short[] shortArr)
        {
            var bytes = MemoryMarshal.AsBytes(shortArr.AsSpan());
            sha256.TransformBlock(bytes.ToArray(), 0, bytes.Length, null, 0);
        }
        else if (o is double[] doubleArr)
        {
            var bytes = MemoryMarshal.AsBytes(doubleArr.AsSpan());
            sha256.TransformBlock(bytes.ToArray(), 0, bytes.Length, null, 0);
        }
        else if (o is byte[] byteArr)
        {
            sha256.TransformBlock(byteArr, 0, byteArr.Length, null, 0);
        }
        else if (o is int[] intArr)
        {
            var bytes = MemoryMarshal.AsBytes(intArr.AsSpan());
            sha256.TransformBlock(bytes.ToArray(), 0, bytes.Length, null, 0);
        }
        else if (o is Array arr && arr.Rank > 1 && arr.GetType().GetElementType()!.IsPrimitive)
        {
            int elementSize = Marshal.SizeOf(arr.GetType().GetElementType()!);
            var flat = new byte[arr.Length * elementSize];
            Buffer.BlockCopy(arr, 0, flat, 0, flat.Length);
            sha256.TransformBlock(flat, 0, flat.Length, null, 0);
        }
        else if (o is Array jaggedArr)
        {
            foreach (var element in jaggedArr)
            {
                if (element != null)
                    HashArrayRecursive(sha256, element);
            }
        }
    }
}
