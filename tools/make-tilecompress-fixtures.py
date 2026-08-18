#!/usr/bin/env python
"""Regenerate the tile-compressed (.fz) test fixtures under
tests/CSharpFITS.Test/testdocs/tilecompress/.

Every fixture is a PAIR:

  <name>.fz       the tile-compressed file CSharpFITS must read
  <name>.ref.fits a plain uncompressed FITS holding the array astropy
                  (i.e. cfitsio) decodes that .fz to -- the oracle

The .fz files are written by astropy, which uses the same tiled-image
convention cfitsio/fpack implement, so they are representative of what
fpack, Siril, SharpCap and the survey archives emit.

Requires: astropy (pip install astropy). Run from the repo root:

    python tools/make-tilecompress-fixtures.py
"""

import os
import numpy as np
from astropy.io import fits

OUT = os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "..", "tests", "CSharpFITS.Test", "testdocs", "tilecompress")

NO_DITHER = -1
SUBTRACTIVE_DITHER_1 = 1
SUBTRACTIVE_DITHER_2 = 2


def emit(name, data, header=None, **kw):
    """Write <name>.fz plus the <name>.ref.fits oracle."""
    os.makedirs(OUT, exist_ok=True)
    fz = os.path.join(OUT, name + ".fz")
    ref = os.path.join(OUT, name + ".ref.fits")
    for p in (fz, ref):
        if os.path.exists(p):
            os.remove(p)

    hdu = fits.CompImageHDU(data=data, header=header, **kw)
    fits.HDUList([fits.PrimaryHDU(), hdu]).writeto(fz)

    with fits.open(fz) as hdul:
        decoded = hdul[1].data
    fits.PrimaryHDU(data=decoded).writeto(ref)

    # Which column actually holds each tile: cfitsio falls back to
    # GZIP_COMPRESSED_DATA (the raw values, gzipped, losslessly) whenever the
    # quantized+compressed form would not be smaller. Both paths are real and
    # both must be exercised, so name the one this fixture takes.
    with fits.open(fz, disable_image_compression=True) as hdul:
        t = hdul[1]
        cols = t.data.columns.names
        used = []
        for c in ("COMPRESSED_DATA", "GZIP_COMPRESSED_DATA"):
            if c in cols and any(len(row) for row in t.data[c]):
                used.append(c)
        zblank = "ZBLANK" if "ZBLANK" in t.header else ""
    print(f"{name:24s} {str(data.dtype):8s} {str(data.shape):16s} "
          f"{kw.get('compression_type', 'RICE_1'):12s} "
          f"{os.path.getsize(fz):7d}B  {'+'.join(used)} {zblank}")


def ramp(shape, dtype, scale=1.0, offset=0.0):
    """Deterministic, structured test pattern: a diagonal ramp plus a few
    bright spots, so a transposed / off-by-one-tile decode is visible."""
    idx = np.indices(shape).astype(np.float64)
    v = sum((i + 1) * a for i, a in enumerate(idx))
    v = v * scale + offset
    v[..., ::17] += 5 * scale          # vertical stripes
    if len(shape) >= 2:
        v[..., 3, :] += 11 * scale     # one hot row
    return v.astype(dtype)


def main():
    rng = np.random.default_rng(20260818)

    # --- integer images: Rice is LOSSLESS here, so the oracle is exact -------
    emit("rice_i16", ramp((64, 96), np.int16, scale=3, offset=-1000),
         compression_type="RICE_1", tile_shape=(8, 96))
    emit("rice_i32", ramp((40, 40), np.int32, scale=1000),
         compression_type="RICE_1", tile_shape=(40, 40))
    emit("rice_u8", ramp((32, 48), np.uint8, scale=1),
         compression_type="RICE_1", tile_shape=(4, 48))

    # partial tiles: neither axis divides evenly
    emit("rice_i16_partial_tiles", ramp((50, 70), np.int16, scale=7),
         compression_type="RICE_1", tile_shape=(17, 23))

    # 3D cube, one tile per row -- the shape Siril writes
    emit("rice_cube", ramp((3, 24, 40), np.int16, scale=5),
         compression_type="RICE_1", tile_shape=(1, 1, 40))

    # noise defeats the low-entropy path and exercises the fs==fsmax branch
    noisy = rng.integers(-30000, 30000, size=(48, 64)).astype(np.int16)
    emit("rice_i16_noise", noisy, compression_type="RICE_1", tile_shape=(6, 64))

    # --- quantized float images: LOSSY, oracle is within one quantum --------
    # Noisy: quantize + Rice actually wins, so COMPRESSED_DATA is populated.
    f = (ramp((64, 64), np.float32, scale=1e-3, offset=0.5)
         + rng.normal(0, 3e-3, (64, 64))).astype(np.float32)
    # Smooth: too compressible to beat the raw gzip of the floats, so cfitsio
    # takes the GZIP_COMPRESSED_DATA fallback -- a path a reader must handle.
    smooth = ramp((64, 64), np.float32, scale=1e-3, offset=0.5)
    emit("rice_f32_dither1", f, compression_type="RICE_1", tile_shape=(8, 64),
         quantize_level=16, quantize_method=SUBTRACTIVE_DITHER_1)
    fz = f.copy()
    fz[10:14, 10:14] = 0.0             # exact zeros: dither-2 must preserve them
    emit("rice_f32_dither2", fz, compression_type="RICE_1", tile_shape=(8, 64),
         quantize_level=16, quantize_method=SUBTRACTIVE_DITHER_2)
    emit("rice_f32_nodither", f, compression_type="RICE_1", tile_shape=(8, 64),
         quantize_level=16, quantize_method=NO_DITHER)
    emit("rice_f64_dither1",
         (ramp((32, 32), np.float64, scale=1e-2, offset=7.0)
          + rng.normal(0, 3e-2, (32, 32))),
         compression_type="RICE_1", tile_shape=(4, 32),
         quantize_level=16, quantize_method=SUBTRACTIVE_DITHER_1)

    # --- gzip: lossless for floats when quantize_level=0 --------------------
    emit("gzip1_f32", smooth, compression_type="GZIP_1", tile_shape=(16, 64),
         quantize_level=0.0)
    emit("gzip2_f32", smooth, compression_type="GZIP_2", tile_shape=(16, 64),
         quantize_level=0.0)
    emit("gzip1_i16", ramp((40, 40), np.int16, scale=9),
         compression_type="GZIP_1", tile_shape=(40, 40))
    emit("gzip2_i32", ramp((24, 24), np.int32, scale=70000),
         compression_type="GZIP_2", tile_shape=(8, 24))

    # --- PLIO: run-length mask compression, positive ints only --------------
    mask = np.zeros((64, 64), dtype=np.int16)
    mask[8:20, 8:40] = 1
    mask[30:33, :] = 7
    mask[40:60, 50:64] = 1000
    emit("plio_i16_mask", mask, compression_type="PLIO_1", tile_shape=(16, 64))

    # --- HCompress ----------------------------------------------------------
    emit("hcompress_i16", ramp((64, 64), np.int16, scale=4),
         compression_type="HCOMPRESS_1", tile_shape=(16, 64), hcomp_scale=0)
    emit("hcompress_i16_smooth", noisy[:32, :32].copy(),
         compression_type="HCOMPRESS_1", tile_shape=(32, 32), hcomp_scale=0)

    # --- null / blank handling ---------------------------------------------
    # NaNs in a tile that still takes the quantize path become ZBLANK-flagged
    # NULL_VALUE integers; the reader must map them back to NaN.
    withnan = f.copy()
    withnan[20:24, 30:36] = np.nan
    emit("rice_f32_nan", withnan, compression_type="RICE_1", tile_shape=(8, 64),
         quantize_level=16, quantize_method=SUBTRACTIVE_DITHER_1)

    # The same NaNs down the gzip-fallback path: they survive as raw float bits.
    smoothnan = smooth.copy()
    smoothnan[20:24, 30:36] = np.nan
    emit("rice_f32_nan_gzipfallback", smoothnan, compression_type="RICE_1",
         tile_shape=(8, 64), quantize_level=16,
         quantize_method=SUBTRACTIVE_DITHER_1)

    # --- a header full of real metadata cards, to pin header translation ----
    hdr = fits.Header()
    hdr["OBJECT"] = ("Bubble Nebula", "Name of the object of interest")
    hdr["EXPTIME"] = (150.0, "[s]  Exposure time duration")
    hdr["INSTRUME"] = "AA585CTEC"
    hdr["ROWORDER"] = ("TOP-DOWN", "Order of the rows in image array")
    hdr["STACKCNT"] = (163, "Stack frames")
    hdr["CCD-TEMP"] = -9.9
    hdr["BAYERPAT"] = "RGGB"
    hdr.add_history("mean stacking with winsorized sigma clipping")
    hdr.add_comment("a comment card that must survive translation")
    emit("rice_i16_metadata", ramp((16, 16), np.int16, scale=2), header=hdr,
         compression_type="RICE_1", tile_shape=(4, 16))


if __name__ == "__main__":
    main()
