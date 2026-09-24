using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;

namespace AssetStudio
{
    public static class Texture2DExtensions
    {
        public static Image<Bgra32> ConvertToImage(this Texture2D m_Texture2D, bool flip)
        {
            var converter = new Texture2DConverter(m_Texture2D);
            var buff = BigArrayPool<byte>.Shared.Rent(m_Texture2D.m_Width * m_Texture2D.m_Height * 4);
            try
            {
                // ArrayPool<T>.Rent does NOT clear the buffer it hands back - it can be full of
                // leftover bytes from a completely unrelated previous rent from this shared pool
                // (another texture, or even BundleFile's decompression buffers, since they share
                // BigArrayPool<byte>). Block-compressed formats (DXT/ETC/ASTC/Crunch) don't
                // always write every byte for textures whose width/height aren't a multiple of
                // the format's block size, which used to leave that stale pool data showing
                // through as visible garbage/noise on the decoded texture. Zeroing first ensures
                // any bytes the decoder doesn't touch come out as transparent black instead.
                Array.Clear(buff, 0, buff.Length);
                if (converter.DecodeTexture2D(buff))
                {
                    var image = Image.LoadPixelData<Bgra32>(buff, m_Texture2D.m_Width, m_Texture2D.m_Height);
                    if (flip)
                    {
                        image.Mutate(x => x.Flip(FlipMode.Vertical));
                    }
                    return image;
                }
                return null;
            }
            finally
            {
                BigArrayPool<byte>.Shared.Return(buff);
            }
        }

        public static MemoryStream ConvertToStream(this Texture2D m_Texture2D, ImageFormat imageFormat, bool flip)
        {
            var image = ConvertToImage(m_Texture2D, flip);
            if (image != null)
            {
                using (image)
                {
                    return image.ConvertToStream(imageFormat);
                }
            }
            return null;
        }
    }
}
