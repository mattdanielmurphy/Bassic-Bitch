using System;
using System.Buffers;
using System.IO;
using Pfim;
using UnityEngine;

namespace Rocksmith2014PsarcLib.Psarc.Asset
{
    public class DdsAsset : PsarcAsset
    {
        /// <summary>
        /// Uses ArrayPool to rent byte arrays to Pfim, by default Pfim creates a new byte array each time
        /// </summary>
        private class ArrayPoolAllocator : IImageAllocator
        {
            // Use the shared byte array pool
            private readonly ArrayPool<byte> pool = ArrayPool<byte>.Shared;
            
            public byte[] Rent(int size)
            {
                return pool.Rent(size);
            }

            public void Return(byte[] data)
            {
                pool.Return(data);
            }
        }
        
        /// <summary>
        /// Config used by Pfim to parse dds assets
        /// </summary>
        private readonly PfimConfig config = new PfimConfig(allocator: new ArrayPoolAllocator());
        
        public Texture2D Texture { get; private set; }

        public override void ReadFrom(MemoryStream stream)
        {
            base.ReadFrom(stream);

            using var image = Pfim.Pfimage.FromStream(stream, config);
            TextureFormat format;

            switch (image.Format)
            {
                case Pfim.ImageFormat.Rgba32:
                    format = TextureFormat.RGBA32;
                    break;
                case Pfim.ImageFormat.Rgb24:
                    format = TextureFormat.RGB24;
                    break;
                default:
                    // see the sample for more details
                    throw new NotImplementedException($"Unsupported image format: {image.Format}");
            }

            // Create a new Texture2D
            Texture = new Texture2D(image.Width, image.Height, format, false);

            // Load the raw data.
            Texture.LoadRawTextureData(image.Data);
            Texture.Apply();
        }
    }
}