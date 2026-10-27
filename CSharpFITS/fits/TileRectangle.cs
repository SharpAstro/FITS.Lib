namespace nom.tam.fits
{
    using System;

    /// <summary>
    /// The pixel rectangle one tile of a tile-compressed image covers, per FITS axis with
    /// NAXIS1 first (the fastest-varying axis; for a 2D image that is x, then y).
    /// Positions are zero-based.
    /// </summary>
    public readonly struct TileRectangle
    {
        private readonly int[] _start;
        private readonly int[] _size;

        internal TileRectangle(int[] start, int[] size)
        {
            _start = start;
            _size = size;
        }

        /// <summary>The origin of the tile in the image, per FITS axis.</summary>
        public int[] Start => (int[])_start.Clone();

        /// <summary>The extent of the tile, per FITS axis; clipped at the image's edges.</summary>
        public int[] Size => (int[])_size.Clone();

        /// <summary>The number of pixels in the tile.</summary>
        public int PixelCount
        {
            get
            {
                int n = 1;
                foreach (int s in _size)
                {
                    n *= s;
                }
                return n;
            }
        }
    }
}
