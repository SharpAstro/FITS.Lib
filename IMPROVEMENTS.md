# FITS.Lib Improvements

Changes since forking from [rwg0/csharpfits](https://github.com/rwg0/csharpfits).

## v4.2.0

### Hybrid arrays for 3D+ images and channel access API

For images with 3 or more dimensions (e.g. multi-channel), `DataArray` now returns a
hybrid structure: a jagged outer `Array[]` of rectangular inner arrays. For example,
a 3-channel 4176x6248 float image returns `Array[3]` of `float[4176, 6248]` instead of
a single `float[3, 4176, 6248]`. This reduces the maximum single allocation from 299MB
to ~100MB, which is much friendlier to the GC and Large Object Heap.

1D and 2D images are unchanged (return `float[]` or `float[,]` respectively).

New API for convenient channel access:

```csharp
ImageHDU hdu = ...;
int n = hdu.ChannelCount;          // 1 for 2D, N for 3D+
float[,] red = (float[,])hdu.GetChannel(0);   // first channel
float[,] green = (float[,])hdu.GetChannel(1); // second channel
```

Also available on `ImageData` directly: `ChannelCount` and `GetChannel(int index)`.

## v4.1.0

### SIMD write path and buffered stream bypass

- **Write methods** (short/int/long/float/double) now use SIMD-vectorized
  `BinaryPrimitives.ReverseEndianness` on .NET 10+ instead of scalar byte shuffling.
  This benefits all write paths including BinaryTable data.
- **WriteRectangularArray** uses chunked zero-copy writes from the array's contiguous
  memory (~4MB chunks), eliminating the full-size flat temp buffer allocation.
- **ReadRectangularArray** bypasses `BufferedStream` for large reads (>128KB), reading
  directly from the underlying `FileStream`.

**Performance** (299MB float 6248x4176x3 image, .NET 10):

| Operation | v4.0.0 | v4.1.0 |
|-----------|--------|--------|
| Read | ~197ms | **~142ms** (-28%) |
| Write | ~3,219ms | **~113ms** (-96%) |

## v4.0.0 (breaking)

### Rectangular arrays for image data

Image data (`ImageData.DataArray`) now returns rectangular multi-dimensional arrays
(e.g. `float[,]`, `float[,,]`) instead of jagged arrays (`float[][]`, `Array[]` of `float[]`).

**Impact**: Code that casts `DataArray` to jagged types like `float[][]` must be updated
to use rectangular types like `float[,]` or work with `Array` generically.

**Performance** (299MB float 6248x4176x3 image):

| Metric | v3.2.0 (jagged) | v4.0.0 (rectangular) |
|--------|-----------------|----------------------|
| Read time | ~285ms | **~142ms** (-50%) |
| Write time | ~3,219ms | **~113ms** (-96%) |
| Heap allocations | ~12,500+ | 1 |
| Read calls | ~12,500 | 1 |

On .NET 10+:
- **Read path**: `MemoryMarshal.GetArrayDataReference` gets a span over the rectangular
  array's contiguous memory. For large images, reads bypass `BufferedStream` and go
  directly to the underlying `FileStream`. SIMD endian swap in place (zero-copy).
- **Write path**: chunked zero-copy writes from rectangular array memory with SIMD
  endian swap via `BinaryPrimitives.ReverseEndianness` (~4MB chunks to `_outBuf`).
  No full-size temp allocation.
- **1D write methods** (short/int/long/float/double) also upgraded to SIMD endian
  swap, benefiting all write paths including BinaryTable.

On netstandard2.0, temporary flat buffers with `Buffer.BlockCopy` are used as fallback.

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
