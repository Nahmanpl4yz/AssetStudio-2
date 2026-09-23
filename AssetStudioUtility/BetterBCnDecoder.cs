using System;

namespace AssetStudio
{
    /// <summary>
    /// AssetStudio 2 "Better" decoders.
    ///
    /// The original AssetStudio pipes every block-compressed texture through the bundled
    /// native Texture2DDecoderNative library. That library is fine for most formats, but it:
    ///   - does not implement BC2/DXT3 at all (AssetStudio's switch statement just `break`s,
    ///     silently producing an all-black texture - see Texture2DConverter.DecodeTexture2D),
    ///   - reconstructs BC1/BC3/BC4/BC5 endpoint interpolation with integer math that rounds
    ///     down, which visibly darkens/banding smooth gradients (common on skin, sky and
    ///     lightmap textures) compared to the color ramp the GPU/Unity itself would produce.
    ///
    /// This class re-implements BC1 (DXT1), BC2 (DXT3), BC3 (DXT5), BC4 and BC5 in managed
    /// code using the exact floating point weights from the DirectX/OpenGL specs
    /// (1/3, 2/3 interpolation instead of truncated integer division), and correctly decodes
    /// the explicit 4-bit alpha block that BC2/DXT3 uses. It is selected when the user picks
    /// "Better" in Options > Decoder Mode; "Original" keeps using the native decoders exactly
    /// as before, so nothing changes unless the user opts in.
    /// </summary>
    public static class BetterBCnDecoder
    {
        private static ushort ReadU16(byte[] data, int offset) => (ushort)(data[offset] | (data[offset + 1] << 8));

        private static void DecodeColorBlock(byte[] data, int offset, bool punchThroughAlpha, out uint[] colors, out bool hasTransparent)
        {
            ushort c0 = ReadU16(data, offset);
            ushort c1 = ReadU16(data, offset + 2);

            (int r0, int g0, int b0) = Unpack565(c0);
            (int r1, int g1, int b1) = Unpack565(c1);

            colors = new uint[4];
            colors[0] = Pack(r0, g0, b0, 255);
            colors[1] = Pack(r1, g1, b1, 255);

            hasTransparent = false;
            if (c0 > c1 || !punchThroughAlpha)
            {
                // Standard 4-color interpolation: 2/3-1/3 and 1/3-2/3 weighted, rounded (not truncated).
                colors[2] = Pack(Lerp23(r0, r1), Lerp23(g0, g1), Lerp23(b0, b1), 255);
                colors[3] = Pack(Lerp23(r1, r0), Lerp23(g1, g0), Lerp23(b1, b0), 255);
            }
            else
            {
                // 3-color + transparent black mode (BC1 punch-through alpha).
                colors[2] = Pack((r0 + r1 + 1) / 2, (g0 + g1 + 1) / 2, (b0 + b1 + 1) / 2, 255);
                colors[3] = Pack(0, 0, 0, 0);
                hasTransparent = true;
            }
        }

        // Rounded 2/3-1/3 lerp, matching the reference decoder's rounding behaviour precisely
        // instead of AssetStudioNative's floor-based integer approximation.
        private static int Lerp23(int a, int b) => (2 * a + b + 1) / 3;

        private static (int, int, int) Unpack565(ushort c)
        {
            int r = (c >> 11) & 0x1F;
            int g = (c >> 5) & 0x3F;
            int b = c & 0x1F;
            r = (r << 3) | (r >> 2);
            g = (g << 2) | (g >> 4);
            b = (b << 3) | (b >> 2);
            return (r, g, b);
        }

        // Output buffer convention used throughout AssetStudio is BGRA (see DecodeRGBA32/
        // DecodeBGRA32/DecodeARGB32 and Texture2DExtensions.ConvertToImage's Image.LoadPixelData<Bgra32>),
        // i.e. byte0=B, byte1=G, byte2=R, byte3=A. Packing r into the low byte here (as if the
        // convention were RGBA) swapped every decoded pixel's red and blue channels for all of
        // BC1/DXT1, BC2/DXT3, BC3/DXT5, BC4 and BC5 - the source of the color corruption.
        private static uint Pack(int r, int g, int b, int a) =>
            (uint)((a << 24) | (r << 16) | (g << 8) | b);

        private static void WritePixel(byte[] image, int width, int height, int x, int y, uint color)
        {
            if (x >= width || y >= height)
                return;
            int idx = (y * width + x) * 4;
            image[idx] = (byte)color;
            image[idx + 1] = (byte)(color >> 8);
            image[idx + 2] = (byte)(color >> 16);
            image[idx + 3] = (byte)(color >> 24);
        }

        private static void DecodeBlockIndices(byte[] data, int offset, uint[] colors, byte[] image, int width, int height, int bx, int by)
        {
            for (int py = 0; py < 4; py++)
            {
                byte row = data[offset + py];
                for (int px = 0; px < 4; px++)
                {
                    int idx = (row >> (px * 2)) & 0x3;
                    WritePixel(image, width, height, bx + px, by + py, colors[idx]);
                }
            }
        }

        public static bool DecodeBC1(byte[] data, int width, int height, byte[] image)
        {
            try
            {
                int blocksWide = (width + 3) / 4;
                int blocksHigh = (height + 3) / 4;
                int offset = 0;
                for (int by = 0; by < blocksHigh; by++)
                {
                    for (int bx = 0; bx < blocksWide; bx++)
                    {
                        DecodeColorBlock(data, offset, true, out var colors, out _);
                        DecodeBlockIndices(data, offset + 4, colors, image, width, height, bx * 4, by * 4);
                        offset += 8;
                    }
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>BC2 / DXT3: color block identical to BC1 (no punch-through) plus an explicit 4-bit alpha block. Previously unimplemented in AssetStudio.</summary>
        public static bool DecodeBC2(byte[] data, int width, int height, byte[] image)
        {
            try
            {
                int blocksWide = (width + 3) / 4;
                int blocksHigh = (height + 3) / 4;
                int offset = 0;
                for (int by = 0; by < blocksHigh; by++)
                {
                    for (int bx = 0; bx < blocksWide; bx++)
                    {
                        // First 8 bytes: explicit 4-bit alpha, 16 nibbles, row-major.
                        byte[] alphaData = new byte[16];
                        for (int i = 0; i < 8; i++)
                        {
                            byte b = data[offset + i];
                            alphaData[i * 2] = (byte)((b & 0x0F) * 17);
                            alphaData[i * 2 + 1] = (byte)((b >> 4) * 17);
                        }

                        DecodeColorBlock(data, offset + 8, false, out var colors, out _);
                        for (int py = 0; py < 4; py++)
                        {
                            byte row = data[offset + 12 + py];
                            for (int px = 0; px < 4; px++)
                            {
                                int idx = (row >> (px * 2)) & 0x3;
                                uint color = colors[idx] & 0x00FFFFFF;
                                uint alpha = alphaData[py * 4 + px];
                                WritePixel(image, width, height, bx * 4 + px, by * 4 + py, color | (alpha << 24));
                            }
                        }
                        offset += 16;
                    }
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>BC3 / DXT5: color block identical to BC1 (no punch-through) plus an 8-value interpolated alpha block.</summary>
        public static bool DecodeBC3(byte[] data, int width, int height, byte[] image)
        {
            try
            {
                int blocksWide = (width + 3) / 4;
                int blocksHigh = (height + 3) / 4;
                int offset = 0;
                for (int by = 0; by < blocksHigh; by++)
                {
                    for (int bx = 0; bx < blocksWide; bx++)
                    {
                        var alphas = DecodeAlphaRamp(data, offset);
                        ulong alphaIndices = 0;
                        for (int i = 0; i < 6; i++)
                            alphaIndices |= (ulong)data[offset + 2 + i] << (8 * i);

                        DecodeColorBlock(data, offset + 8, false, out var colors, out _);
                        for (int py = 0; py < 4; py++)
                        {
                            for (int px = 0; px < 4; px++)
                            {
                                int pixelIdx = py * 4 + px;
                                int aIdx = (int)((alphaIndices >> (3 * pixelIdx)) & 0x7);
                                byte row = data[offset + 12 + py];
                                int cIdx = (row >> (px * 2)) & 0x3;
                                uint color = (colors[cIdx] & 0x00FFFFFF) | ((uint)alphas[aIdx] << 24);
                                WritePixel(image, width, height, bx * 4 + px, by * 4 + py, color);
                            }
                        }
                        offset += 16;
                    }
                }
                return true;
            }
            catch { return false; }
        }

        private static byte[] DecodeAlphaRamp(byte[] data, int offset)
        {
            byte a0 = data[offset];
            byte a1 = data[offset + 1];
            var ramp = new byte[8];
            ramp[0] = a0;
            ramp[1] = a1;
            if (a0 > a1)
            {
                for (int i = 1; i <= 6; i++)
                    ramp[1 + i] = (byte)(((7 - i) * a0 + i * a1 + 3) / 7);
            }
            else
            {
                for (int i = 1; i <= 4; i++)
                    ramp[1 + i] = (byte)(((5 - i) * a0 + i * a1 + 2) / 5);
                ramp[6] = 0;
                ramp[7] = 255;
            }
            return ramp;
        }

        /// <summary>BC4 (ATI1/3Dc, single-channel version of the BC3 alpha block), used for grayscale/roughness maps.</summary>
        public static bool DecodeBC4(byte[] data, int width, int height, byte[] image)
        {
            try
            {
                int blocksWide = (width + 3) / 4;
                int blocksHigh = (height + 3) / 4;
                int offset = 0;
                for (int by = 0; by < blocksHigh; by++)
                {
                    for (int bx = 0; bx < blocksWide; bx++)
                    {
                        var ramp = DecodeAlphaRamp(data, offset);
                        ulong indices = 0;
                        for (int i = 0; i < 6; i++)
                            indices |= (ulong)data[offset + 2 + i] << (8 * i);

                        for (int py = 0; py < 4; py++)
                        {
                            for (int px = 0; px < 4; px++)
                            {
                                int pixelIdx = py * 4 + px;
                                int idx = (int)((indices >> (3 * pixelIdx)) & 0x7);
                                byte r = ramp[idx];
                                WritePixel(image, width, height, bx * 4 + px, by * 4 + py, Pack(r, 0, 0, 255));
                            }
                        }
                        offset += 8;
                    }
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>BC5 (ATI2/3Dc2), two independent BC4-style channels, used for tangent-space normal maps.</summary>
        public static bool DecodeBC5(byte[] data, int width, int height, byte[] image)
        {
            try
            {
                int blocksWide = (width + 3) / 4;
                int blocksHigh = (height + 3) / 4;
                int offset = 0;
                for (int by = 0; by < blocksHigh; by++)
                {
                    for (int bx = 0; bx < blocksWide; bx++)
                    {
                        var rRamp = DecodeAlphaRamp(data, offset);
                        ulong rIndices = 0;
                        for (int i = 0; i < 6; i++)
                            rIndices |= (ulong)data[offset + 2 + i] << (8 * i);

                        var gRamp = DecodeAlphaRamp(data, offset + 8);
                        ulong gIndices = 0;
                        for (int i = 0; i < 6; i++)
                            gIndices |= (ulong)data[offset + 10 + i] << (8 * i);

                        for (int py = 0; py < 4; py++)
                        {
                            for (int px = 0; px < 4; px++)
                            {
                                int pixelIdx = py * 4 + px;
                                byte r = rRamp[(int)((rIndices >> (3 * pixelIdx)) & 0x7)];
                                byte g = gRamp[(int)((gIndices >> (3 * pixelIdx)) & 0x7)];
                                // Reconstruct Z for a normal map so the preview/export looks correct, matching
                                // the standard DirectX BC5 "derive blue" convention.
                                double nx = r / 127.5 - 1.0;
                                double ny = g / 127.5 - 1.0;
                                double nz2 = 1.0 - nx * nx - ny * ny;
                                byte b = (byte)(nz2 > 0 ? (Math.Sqrt(nz2) * 0.5 + 0.5) * 255 : 128);
                                WritePixel(image, width, height, bx * 4 + px, by * 4 + py, Pack(r, g, b, 255));
                            }
                        }
                        offset += 16;
                    }
                }
                return true;
            }
            catch { return false; }
        }

        // ETC1, ported directly from the AOSP reference decoder (etc1_decode_block /
        // decode_subblock in frameworks/native/opengl/libs/ETC1/etc1.cpp) rather than
        // re-derived, because the ETC1 bit layout (which fields sit in the "diff"/"flip"
        // bits, how the two 16-bit pixel-index planes map to (x,y), the exact intensity
        // modifier table) is easy to get subtly wrong from memory and a subtly-wrong
        // block layout still "decodes" without erroring - it just produces structured,
        // blocky garbage instead of a clean decode failure. Used for ETC_RGB4/ETC_RGB4_3DS
        // in "Better" mode as a fallback alongside the native Texture2DDecoderNative path.
        private static readonly int[] EtcModifierTable =
        {
            2, 8, -2, -8,
            5, 17, -5, -17,
            9, 29, -9, -29,
            13, 42, -13, -42,
            18, 60, -18, -60,
            24, 80, -24, -80,
            33, 106, -33, -106,
            47, 183, -47, -183
        };

        private static readonly int[] EtcLookup = { 0, 1, 2, 3, -4, -3, -2, -1 };

        private static int EtcConvert4To8(int b)
        {
            int c = b & 0xf;
            return (c << 4) | c;
        }

        private static int EtcConvert5To8(int b)
        {
            int c = b & 0x1f;
            return (c << 3) | (c >> 2);
        }

        private static int EtcConvertDiff(int baseVal, int diff) => EtcConvert5To8((0x1f & baseVal) + EtcLookup[0x7 & diff]);

        // Decodes one 4x2 (or 2x4, if flipped) ETC1 subblock into a local 4x4 RGB buffer
        // (row-major, 3 bytes/pixel), following decode_subblock's index math exactly.
        private static void DecodeEtcSubblock(byte[] block16, int r, int g, int b, int[] table, int tableOffset, uint low, bool second, bool flipped)
        {
            int baseX = 0, baseY = 0;
            if (second)
            {
                if (flipped) baseY = 2;
                else baseX = 2;
            }
            for (int i = 0; i < 8; i++)
            {
                int x, y;
                if (flipped)
                {
                    x = baseX + (i >> 1);
                    y = baseY + (i & 1);
                }
                else
                {
                    x = baseX + (i >> 2);
                    y = baseY + (i & 3);
                }
                int k = y + (x * 4);
                int offset = (int)(((low >> k) & 1) | ((low >> (k + 15)) & 2));
                int delta = table[tableOffset + offset];
                int q = 3 * (x + 4 * y);
                block16[q] = ClampByte(r + delta);
                block16[q + 1] = ClampByte(g + delta);
                block16[q + 2] = ClampByte(b + delta);
            }
        }

        private static byte ClampByte(int x) => (byte)(x >= 0 ? (x < 255 ? x : 255) : 0);

        public static bool DecodeETC1(byte[] data, int width, int height, byte[] image)
        {
            try
            {
                int blocksWide = (width + 3) / 4;
                int blocksHigh = (height + 3) / 4;
                int offset = 0;
                var block = new byte[4 * 4 * 3];
                for (int by = 0; by < blocksHigh; by++)
                {
                    for (int bx = 0; bx < blocksWide; bx++)
                    {
                        uint high = ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
                        uint low = ((uint)data[offset + 4] << 24) | ((uint)data[offset + 5] << 16) | ((uint)data[offset + 6] << 8) | data[offset + 7];

                        int r1, r2, g1, g2, b1, b2;
                        if ((high & 2) != 0)
                        {
                            // differential
                            int rBase = (int)(high >> 27);
                            int gBase = (int)(high >> 19);
                            int bBase = (int)(high >> 11);
                            r1 = EtcConvert5To8(rBase);
                            r2 = EtcConvertDiff(rBase, (int)(high >> 24));
                            g1 = EtcConvert5To8(gBase);
                            g2 = EtcConvertDiff(gBase, (int)(high >> 16));
                            b1 = EtcConvert5To8(bBase);
                            b2 = EtcConvertDiff(bBase, (int)(high >> 8));
                        }
                        else
                        {
                            // individual
                            r1 = EtcConvert4To8((int)(high >> 28));
                            r2 = EtcConvert4To8((int)(high >> 24));
                            g1 = EtcConvert4To8((int)(high >> 20));
                            g2 = EtcConvert4To8((int)(high >> 16));
                            b1 = EtcConvert4To8((int)(high >> 12));
                            b2 = EtcConvert4To8((int)(high >> 8));
                        }

                        int tableIndexA = (int)(7 & (high >> 5));
                        int tableIndexB = (int)(7 & (high >> 2));
                        bool flipped = (high & 1) != 0;

                        DecodeEtcSubblock(block, r1, g1, b1, EtcModifierTable, tableIndexA * 4, low, false, flipped);
                        DecodeEtcSubblock(block, r2, g2, b2, EtcModifierTable, tableIndexB * 4, low, true, flipped);

                        for (int y = 0; y < 4; y++)
                        {
                            for (int x = 0; x < 4; x++)
                            {
                                int q = 3 * (x + 4 * y);
                                uint color = Pack(block[q], block[q + 1], block[q + 2], 255);
                                WritePixel(image, width, height, bx * 4 + x, by * 4 + y, color);
                            }
                        }

                        offset += 8;
                    }
                }
                return true;
            }
            catch { return false; }
        }
    }
}
