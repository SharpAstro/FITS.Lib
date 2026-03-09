using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using nom.tam.fits;

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

        Console.WriteLine($"File: {fitsFilePath}");
        Console.WriteLine($"Size: {new FileInfo(fitsFilePath).Length / (1024.0 * 1024.0):F1} MB");
        Console.WriteLine();

        // Warmup
        {
            var fits = new Fits(fitsFilePath);
            var hdus = fits.Read();
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
        const int dataRuns = 3;
        string? hash = null;
        sw.Restart();
        for (int i = 0; i < dataRuns; i++)
        {
            var fits = new Fits(fitsFilePath);
            var hdus = fits.Read();
            object? data = null;
            foreach (var hdu in hdus)
            {
                if (hdu is ImageHDU imageHdu)
                {
                    data = imageHdu.Data.DataArray;
                }
            }

            // Hash on last run to verify correctness
            if (i == dataRuns - 1 && data != null)
            {
                hash = ComputeImageHash(data);
            }

            fits.Close();
        }
        sw.Stop();
        Console.WriteLine($"Full data load:        {sw.ElapsedMilliseconds / dataRuns:F1} ms avg ({dataRuns} runs)");
        Console.WriteLine($"Image data SHA256:     {hash}");
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
        else if (o is Array arr)
        {
            foreach (var element in arr)
            {
                if (element != null)
                    HashArrayRecursive(sha256, element);
            }
        }
    }
}
