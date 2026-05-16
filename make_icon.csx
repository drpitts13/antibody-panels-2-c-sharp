// csx script: converts PNG to multi-size ICO and creates desktop shortcut
// Run with: dotnet script make_icon.csx  (or just run the PowerShell alternative)
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

var pngPath = @"c:\Mac\Home\Documents\antibody-panels-2-c-sharp\AntibodyPanels\blood_bag_icon.png";
var icoPath = @"c:\Mac\Home\Documents\antibody-panels-2-c-sharp\AntibodyPanels\app.ico";

// Create ICO with multiple sizes
using var src = new Bitmap(pngPath);
var sizes = new[] { 256, 128, 64, 48, 32, 16 };
using var fs = new FileStream(icoPath, FileMode.Create);
using var bw = new BinaryWriter(fs);

// ICO header
bw.Write((short)0);        // Reserved
bw.Write((short)1);        // Type: ICO
bw.Write((short)sizes.Length); // Count

var bitmapData = new List<byte[]>();
foreach (var size in sizes)
{
    using var resized = new Bitmap(src, size, size);
    using var ms = new MemoryStream();
    resized.Save(ms, ImageFormat.Png);
    bitmapData.Add(ms.ToArray());
}

int offset = 6 + sizes.Length * 16;
for (int i = 0; i < sizes.Length; i++)
{
    int sz = sizes[i] == 256 ? 0 : sizes[i];
    bw.Write((byte)sz);     // Width
    bw.Write((byte)sz);     // Height
    bw.Write((byte)0);      // Color count
    bw.Write((byte)0);      // Reserved
    bw.Write((short)1);     // Planes
    bw.Write((short)32);    // Bit count
    bw.Write(bitmapData[i].Length);  // Size
    bw.Write(offset);                // Offset
    offset += bitmapData[i].Length;
}
foreach (var data in bitmapData)
    bw.Write(data);

Console.WriteLine($"ICO written: {icoPath}");
