Fork of the CSharpFITS package from http://voi.iucaa.in/voi/CSharpFITS.html

This fork basically adds a big performance boost for the common use case of writing a large, probably multi-dimensional, array of int or short values.

## Tile-compressed images (`.fz`)

Files written by `fpack` -- and by Siril, SharpCap, astropy and the survey archives --
store the image as a binary table of compressed tiles. They read back as the image
they hold, with no special handling by the caller:

```csharp
var fits = new Fits("stack.fit.fz");
fits.ReadHDU();                          // the empty primary HDU fpack writes
var hdu = (ImageHDU)fits.ReadHDU();      // a CompressedImageHDU

hdu.BitPix;                              // -32
hdu.Axes;                                // { 3, 2160, 3840 }
hdu.Header.GetStringValue("OBJECT");     // the image's own metadata
hdu.GetChannel(0);                       // float[2160, 3840]
```

`RICE_1`, `GZIP_1`, `GZIP_2`, `PLIO_1` and `NOCOMPRESS` are supported, along with
quantized floating-point images under all three dithering methods. `HCOMPRESS_1` is
recognised but not decoded. Writing compresses nothing -- an HDU writes out as the
plain image extension it presents, making a read plus a write a funpack. See
[CHANGELOG.md](CHANGELOG.md) for the details and the limits.

README FOR CSharpFITS package source code distribution
------------------------------------------------------
1.Souce code zip contains the visual studio 2005 project.
(CSharpFITS_v1.1 folder)
2.Source code can be viewed with anything capable of reading ASCII.
3.Visual Studio 2005 is required to open and compile the project.

Refer docs folder for help on using the API.. 	
