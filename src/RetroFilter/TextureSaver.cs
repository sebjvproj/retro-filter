using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RetroFilter;

/// <summary>Enregistre une texture BGRA du GPU en fichier PNG.</summary>
static class TextureSaver
{
    public static void SavePng(ID3D11Device device, ID3D11Texture2D texture, int width, int height, string path)
    {
        using var staging = device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read,
        });

        var context = device.ImmediateContext;
        context.CopySubresourceRegion(staging, 0, 0, 0, 0, texture, 0, new Vortice.Mathematics.Box(0, 0, 0, width, height, 1));

        var mapped = context.Map(staging, 0, MapMode.Read);
        try
        {
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            var row = new byte[width * 4];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(mapped.DataPointer + y * (int)mapped.RowPitch, row, 0, row.Length);
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
            }
            bitmap.UnlockBits(data);
            bitmap.Save(path, ImageFormat.Png);
        }
        finally
        {
            context.Unmap(staging, 0);
        }
    }
}
