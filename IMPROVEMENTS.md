# FITS.Lib Improvements

Changes since forking from [rwg0/csharpfits](https://github.com/rwg0/csharpfits).

## v4.0.0 (breaking)

### Rectangular arrays for image data

Image data (`ImageData.DataArray`) now returns rectangular multi-dimensional arrays
(e.g. `float[,]`, `float[,,]`) instead of jagged arrays (`float[][]`, `Array[]` of `float[]`).

**Impact**: Code that casts `DataArray` to jagged types like `float[][]` must be updated
to use rectangular types like `float[,]` or work with `Array` generically.

**Performance** (299MB float 6248x4176x3 image):

| Metric | v3.2.0 (jagged) | v4.0.0 (rectangular) |
|--------|-----------------|----------------------|
| Load time | ~285ms | ~197ms (**-31%**) |
| Heap allocations | ~12,500+ | 1 |
| Read calls | ~12,500 | 1 |

On .NET 10+, the read path uses `MemoryMarshal.GetArrayDataReference` to get a span
over the rectangular array's contiguous memory, then reads and endian-swaps directly
in place (zero-copy). On netstandard2.0, a temporary flat buffer is used with `Buffer.BlockCopy`.

Non-image data (BinaryTable, AsciiTable, RandomGroups) is unchanged and still uses
jagged arrays.

### Bug fix: read-only file access NullReferenceException

Opening a FITS file read-only (`new BufferedFile(path)` or `new Fits(path, FileAccess.Read)`)
no longer throws a `NullReferenceException` on `Close()` or `Flush()`. The `BinaryWriter`
is now null-guarded since it is not created for read-only streams.

Also fixed `BufferedFile` to use `FileMode.Open` instead of `FileMode.OpenOrCreate` for
read-only access, preventing accidental creation of empty files.

## v3.2.0

### Read performance optimization (~37% faster)

- .NET 10 read methods use zero-allocation SIMD endian swap via `MemoryMarshal.AsBytes`
  and `BinaryPrimitives.ReverseEndianness` (vectorized in-place byte reversal)
- Increased `BufferedFile` buffer from 32KB to 128KB
- Added benchmark project at `tests/CSharpFITS.Benchmark/`

## v3.1.0

### Rectangular array write support

Added `WriteRectangularArray` to `BufferedDataStream` for writing multi-dimensional
arrays via `Buffer.BlockCopy` flattening.

## Earlier changes

- Upgraded to .NET 10 + netstandard2.0 multi-targeting
- Deterministic builds
- Added test coverage (NUnit)
- Merged improvements from [rwg0](https://github.com/rwg0/csharpfits):
  alphabetical header ordering, junk-after-HDU tolerance, short string header fix
