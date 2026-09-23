using System;
using System.Runtime.InteropServices;
using AssetStudio.PInvoke;

namespace Texture2DDecoder
{
    public static unsafe partial class TextureDecoder
    {

        static TextureDecoder()
        {
            DllLoader.PreloadDll(T2DDll.DllName);
        }

        public static bool DecodeDXT1(byte[] data, int width, int height, byte[] image)
        {
            fixed (byte* pData = data)
            {
                fixed (byte* pImage = image)
                {
                    return DecodeDXT1(pData, width, height, pImage);
                }
            }
        }

        public static bool DecodeDXT5(byte[] data, int width, int height, byte[] image)
        {
            fixed (byte* pData = data)
            {
                fixed (byte* pImage = image)
                {
                    return DecodeDXT5(pData, width, height, pImage);
                }
            }
        }

        public static bool DecodePVRTC(byte[] data, int width, int height, byte[] image, bool is2bpp)
        {
            fixed (byte* pData = data)
            {
                fixed (byte* pImage = image)
                {
                    return DecodePVRTC(pData, width, height, pImage, is2bpp);
                }
            }
        }

        public static bool DecodeETC1(byte[] data, int width, int height, byte[] image)
        {
            fixed (byte* pData = data)
            {
                fixed (byte* pImage = image)
                {
                    return DecodeETC1(pData, width, height, pImage);
                }
            }
        }

        public static bool DecodeETC2(byte[] data, int width, int height, byte[] image)
        {
            fixed (byte* pData = data)
            {
                fixed (byte* pImage = image)
                {
                    return DecodeETC2(pData, width, height, pImage);
                }
            }
        }

        public static bool DecodeETC2A1(byte[] data, int width, int height, byte[] image)
        {
            fixed (byte* pData = data)
            {
                fixed (byte* pImage = image)
                {
                    return DecodeETC2A1(pData, width, height, pImage);
                }
            }
        }

        public static bool DecodeETC2A8(byte[] data, int width, int height, byte[] image)
        {
            fixed (byte* pData = data)
            {
                fixed (byte* pImage = image)
                {
                    return DecodeETC2A8(pData, width, height, pImage);
                }
            }
        }

        public static bool DecodeEACR(byte[] data, int width, int height, byte[] image)
        {
            fixed (byte* pData = data)
            {
                fixed (byte* pImage = image)
                {
                    return DecodeEACR(pData, width, height, pImage);
                }
            }
        }

        public static bool DecodeEACRSigned(byte[] data, int width, int height, byte[] image)
        {
            fixed (byte* pData = data)
            {
                fixed (byte* pImage = image)
                {
                    return DecodeEACRSigned(pData, width, height, pImage);
                }
            }
        }

        public static bool DecodeEACRG(byte[] data, int width, int height, byte[] image)
        {
            fixed (byte* pData = data)
            {
                fixed (byte* pImage = image)
                {
                    return DecodeEACRG(pData, width, height, pImage);
                }
            }
        }

        public static bool DecodeEACRGSigned(byte[] data, int width, int height, byte[] image)
        {
            fixed (byte* pData = data)
            {
                fixed (byte* pImage = image)
                {
                    return DecodeEACRGSigned(pData, width, height, pImage);
                }
            }
        }

        public static bool DecodeBC4(byte[] data, int width, int height, byte[] image)
        {
            fixed (byte* pData = data)
            {
                fixed (byte* pImage = image)
                {
                    return DecodeBC4(pData, width, height, pImage);
                }
            }
        }

        public static bool DecodeBC5(byte[] data, int width, int height, byte[] image)
        {
            fixed (byte* pData = data)
            {
                fixed (byte* pImage = image)
                {
                    return DecodeBC5(pData, width, height, pImage);
                }
            }
        }

        public static bool DecodeBC6(byte[] data, int width, int height, byte[] image)
        {
            fixed (byte* pData = data)
            {
                fixed (byte* pImage = image)
                {
                    return DecodeBC6(pData, width, height, pImage);
                }
            }
        }

        public static bool DecodeBC7(byte[] data, int width, int height, byte[] image)
        {
            fixed (byte* pData = data)
            {
                fixed (byte* pImage = image)
                {
                    return DecodeBC7(pData, width, height, pImage);
                }
            }
        }

        public static bool DecodeATCRGB4(byte[] data, int width, int height, byte[] image)
        {
            fixed (byte* pData = data)
            {
                fixed (byte* pImage = image)
                {
                    return DecodeATCRGB4(pData, width, height, pImage);
                }
            }
        }

        public static bool DecodeATCRGBA8(byte[] data, int width, int height, byte[] image)
        {
            fixed (byte* pData = data)
            {
                fixed (byte* pImage = image)
                {
                    return DecodeATCRGBA8(pData, width, height, pImage);
                }
            }
        }

        public static bool DecodeASTC(byte[] data, int width, int height, int blockWidth, int blockHeight, byte[] image)
        {
            fixed (byte* pData = data)
            {
                fixed (byte* pImage = image)
                {
                    return DecodeASTC(pData, width, height, blockWidth, blockHeight, pImage);
                }
            }
        }

        public static byte[] UnpackCrunch(byte[] data) => UnpackCrunch(data, data.Length);

        // dataLength must be the number of VALID compressed bytes in data, not data.Length -
        // data commonly comes from an ArrayPool<byte> rental (see Texture2DConverter.DecodeTexture2D),
        // and ArrayPool.Rent is only guaranteed to return an array at least as big as requested, so
        // data.Length can run past the real payload into stale bytes left over from a previous rental.
        // Feeding those extra bytes to the native crunch decompressor desyncs it partway through and
        // produces corrupted/noisy output, regardless of which BCn decoder mode is used afterwards.
        public static byte[] UnpackCrunch(byte[] data, int dataLength)
        {
            void* pBuffer;
            uint bufferSize;

            fixed (byte* pData = data)
            {
                UnpackCrunch(pData, (uint)dataLength, out pBuffer, out bufferSize);
            }

            if (pBuffer == null)
            {
                return null;
            }

            var result = new byte[bufferSize];

            Marshal.Copy(new IntPtr(pBuffer), result, 0, (int)bufferSize);

            DisposeBuffer(ref pBuffer);

            return result;
        }

        public static byte[] UnpackUnityCrunch(byte[] data) => UnpackUnityCrunch(data, data.Length);

        // See the note on UnpackCrunch(byte[], int) above - dataLength must be the real payload size,
        // not data.Length, when data is a pooled/oversized array.
        public static byte[] UnpackUnityCrunch(byte[] data, int dataLength)
        {
            void* pBuffer;
            uint bufferSize;

            fixed (byte* pData = data)
            {
                UnpackUnityCrunch(pData, (uint)dataLength, out pBuffer, out bufferSize);
            }

            if (pBuffer == null)
            {
                return null;
            }

            var result = new byte[bufferSize];

            Marshal.Copy(new IntPtr(pBuffer), result, 0, (int)bufferSize);

            DisposeBuffer(ref pBuffer);

            return result;
        }

    }
}
