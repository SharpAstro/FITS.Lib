# Changelog

Newest first. The version is set in one place, `VersionMajorMinor` in
`Directory.Build.props`; CI appends the run number to it.

Releases before 5.0 predate this file; `git log` is their record.

## 6.1

**`FitsWriter` writes an image forward-only onto any writable stream, with no frame-sized buffer**
(net10.0; the netstandard2.0 build does not have it). Additive.

```csharp
var header = FitsWriter.ImageHeader(bitpix: 16, width, height); // NAXIS1 first
header.AddValue("OBJECT", "M 42", "");
using var writer = FitsWriter.CreateFile(path, header);         // header written
writer.WriteInPlace(band);                                     // as many bands as you like
writer.Finish();                                               // checks the length, pads to 2880
```

- **The same bytes as `Fits.Write`.** The header is formatted by `Header.Write` itself, and
  `ImageHeader` is the primary header the old path writes (the setter that makes a first HDU
  primary is applied, not copied). Tests compare every BITPIX, a cube of planes, a write split into
  any number of calls, a gzip stream and a file against `Fits.Write`, byte for byte.
- **Any writable stream.** A write-only `GZipStream` works; the old path refused one, because it
  builds a `BinaryReader` over any stream it is handed.
- **No frame-sized buffer.** `Write<T>` swaps to big-endian through one rented 2 MB band;
  `WriteInPlace<T>` swaps the caller's own scratch instead. A caller that produces the data band by
  band, such as a quantised copy of a float frame, never holds an array the size of the image.
- **`CreateFile`** opens the file unbuffered and preallocated to its final length. Every write is a
  whole header, a band or the padding, so a buffer would only copy them.
- **Order is enforced.** Data before the header, a second header, a sample type BITPIX does not
  name, more data than the header declares, and a `Finish` short of it all throw.

**The old writer allocates less.** `Fits.Write` swapped a rectangular array to big-endian through
`_outBuf`, a field grown per stream to a 1M-element chunk. A stream is usually made for one write, so
that was 2 to 8 MB of large-object garbage per write, even for a 100 x 100 image. The chunk is now
rented, and no larger than the image.

## 6.0

`PartialFitsReader.ReadRegion` no longer takes a `System.Drawing.Rectangle`.

**Breaking.** The rectangle overload is gone and the region is stated as plain edges:

```csharp
// before
reader.ReadRegion(new Rectangle(x, y, width, height), dest);
// after
reader.ReadRegion(x, y, width, height, dest);
```

The old signature forced every caller to name a drawing type purely to carry four
integers. That type is portable in itself, but a consumer that deliberately keeps the
namespace out of its own imaging and device layers could not call this reader without
reintroducing it, and this was the last thing in the library referencing it (through a
`using Rectangle = System.Drawing.Rectangle;` alias, now deleted).

**The pixels a given region returns do not change.** The five decode paths are
untouched: the bounds check, the row loops and the `BZERO`/`BSCALE` arithmetic are
what they were, with an internal `PixelRegion` carrying the same
`X`/`Y`/`Width`/`Height`/`Right`/`Bottom` shape, and `Right`/`Bottom` remain
**exclusive**.

## 5.0

Tile-compressed images (`.fz`) can be read.

A file written by `fpack` -- or by Siril, SharpCap, astropy, or pulled from a
survey archive -- stores its image as a binary table of compressed tiles, with the
real image header carried alongside in `Z`-prefixed keywords. Such an HDU now reads
back as the image it holds:

```csharp
var fits = new Fits("stack.fit.fz");
fits.ReadHDU();                             // the empty primary fpack writes
var hdu = (ImageHDU)fits.ReadHDU();         // a CompressedImageHDU

hdu.BitPix;                                 // -32, from ZBITPIX
hdu.Axes;                                   // { 3, 2160, 3840 }, from ZNAXISn
hdu.Header.GetStringValue("OBJECT");        // the image's own metadata
hdu.GetChannel(0);                          // float[2160, 3840]
```

`BITPIX`, `NAXIS`, `NAXISn` and every metadata card read exactly as they would from
an uncompressed file, and `GetChannel`, `Tiler` and `Data.DataArray` behave
identically, so a reader that already handles FITS images needs no changes to handle
`.fz`. Decompression is deferred until the pixels are asked for, the way an
uncompressed image's read is, so `ReadHDUHeaderOnly` still costs nothing.

- **Algorithms**: `RICE_1`, `GZIP_1`, `GZIP_2`, `PLIO_1` and `NOCOMPRESS`, plus the
  `GZIP_COMPRESSED_DATA` fallback a writer uses for a tile that would not compress.
  Floating-point images are dequantized with `NO_DITHER`, `SUBTRACTIVE_DITHER_1` and
  `SUBTRACTIVE_DITHER_2`, including the exactly-preserved zeros dither 2 exists for,
  and `ZBLANK` pixels come back as NaN.
- **Not supported**: `HCOMPRESS_1`, and images carrying a `NULL_PIXEL_MASK` column.
  Both are recognised and reported by name rather than silently mis-decoded.
- **Writing compresses nothing.** The HDU writes out as the plain image extension it
  presents itself as, so a read followed by a write is a funpack.
- Checked against cfitsio rather than against itself: every fixture under
  `tests/CSharpFITS.Test/testdocs/tilecompress/` is a `.fz` paired with the array
  cfitsio decodes it to, and the tests compare against that. Lossless fixtures must
  match exactly; a quantized one may differ only in the last bit of the float.
  Regenerate them with `tools/make-tilecompress-fixtures.py`.

### Breaking

- A binary table carrying `ZIMAGE = T` now produces a `CompressedImageHDU` (which is
  an `ImageHDU`) instead of a `BinaryTableHDU`. Code that reached into such an HDU as
  a table -- to inspect or copy the compressed form rather than the pixels -- should
  set `FitsFactory.UseTileCompression = false`, which restores the old behaviour.

### Fixed

- **`BasicHDU.ObservationDate` and `CreationDate` threw `NullReferenceException` for a date they
  could not parse.** The getter caught the `FitsException`, assigned `null`, and then cast that to
  `DateTime` -- so the handler written to tolerate a bad date was itself the crash, out of a property
  getter. An absent card already returned `default` (the parser returns early for null input), so the
  unparseable case now agrees with it instead of being fatal. The doc comment promising "either null
  or a Date object" came from the Java original, where these returned a reference; a `DateTime` cannot
  be null, and the comment was describing the bug.

- **An old-style `DD/MM/YY` date with a single-digit day did not parse.** The convention pads the day
  with a space, so a real card reads `DATE-OBS = ' 2/07/96'`, which trims to seven characters -- and
  the parser required eight, a minimum that suits the new `yyyy-mm-dd` style and rejects the old one
  outright. Single-digit days and months are now accepted; a missing day (`/09/79`), missing month
  (`09//79`) or missing year (`20/09/`) is still rejected. Together with the fix above this is what
  made NASA's own reference sample `FOCx38i0101t_c0f.fits` unreadable rather than merely undated.

- `Header.TrueDataSize` reported one 2880-byte block for an HDU with `NAXIS = 0`
  instead of no data at all, because an empty product of axes came out as one pixel.
  Anything skipping data by that size -- `ReadHDUHeaderOnly`, `SkipHDU` -- landed a
  block past the next header. Every `fpack`-compressed file begins with exactly such
  an empty primary HDU, and so does plenty of ordinary FITS.
