using BenchmarkDotNet.Attributes;
using nom.tam.fits;

namespace CSharpFITS.Benchmark;

[MemoryDiagnoser]
public class FitsLoadBenchmark
{
    private string _fitsFilePath = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Resolve the test file path relative to the repository root
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "CSharpFITS.sln")))
            dir = Path.GetDirectoryName(dir);

        _fitsFilePath = Path.Combine(dir!, "tests", "CSharpFITS.Test", "testdocs", "LDN1089_singleFrame.fits");

        if (!File.Exists(_fitsFilePath))
            throw new FileNotFoundException($"Test FITS file not found: {_fitsFilePath}");
    }

    [Benchmark(Description = "Open FITS (header only, deferred data)")]
    public BasicHDU[] OpenFitsDeferred()
    {
        var fits = new Fits(_fitsFilePath);
        var hdus = fits.Read();
        fits.Close();
        return hdus;
    }

    [Benchmark(Description = "Load FITS with full image data")]
    public object? LoadFitsWithData()
    {
        var fits = new Fits(_fitsFilePath);
        var hdus = fits.Read();
        // Force the deferred image data to be read
        object? data = null;
        foreach (var hdu in hdus)
        {
            if (hdu is ImageHDU imageHdu)
            {
                data = imageHdu.Data.DataArray;
            }
        }
        fits.Close();
        return data;
    }
}
