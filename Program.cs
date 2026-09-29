using System.Diagnostics;
using SkiaSharp;

namespace P8SpritesheetExtract
{
    class Program
    {
        static void Main(string[] args)
        {
            Config config = new(args);

            P8GfxData gfxData = new(config.input_path);

            SKBitmap bitmap = gfxData.ToBitmap();

            using (FileStream stream = new(config.output_path, FileMode.Create, FileAccess.Write))
            {
                bitmap.Encode(stream, SKEncodedImageFormat.Png, 0);
            };
        }
    }

    class Config
    {
        public readonly string input_path;
        public readonly string output_path;

        public Config(string[] args)
        {
            if (args.Length < 1)
            {
                throw new Exception("pico-8 file not specified");
            }
            else if (args.Length < 2)
            {
                throw new Exception("output file not specified");
            }

            try
            {
                FileInfo _ = new(args[0]);
            }
            catch (Exception exception)
            {
                if (
                    exception is ArgumentException ||
                    exception is PathTooLongException ||
                    exception is NotSupportedException
                )
                {
                    throw new Exception("invalid input file path");
                }
                else
                {
                    throw;
                }
            }

            input_path = args[0];

            try
            {
                FileInfo _ = new(args[1]);
            }
            catch (Exception exception)
            {
                if (
                    exception is ArgumentException ||
                    exception is PathTooLongException ||
                    exception is NotSupportedException
                )
                {
                    throw new Exception("invalid output file path");
                }
                else
                {
                    throw;
                }
            }

            output_path = Path.GetExtension(args[1]) switch
            {
                ".png" => args[1],
                "" => $"{args[1]}.png",
                _ => $"{Path.GetFileNameWithoutExtension(args[1])}.png"
            };
        }
    }

    class P8GfxData
    {
        public byte[] data;

        public P8GfxData(string path)
        {
            if (Path.GetExtension(path) == ".p8")
            {
                data = P8ReadGfxData(path);
            }
            else if (Path.GetExtension(path) == ".png" && Path.GetExtension(Path.GetFileNameWithoutExtension(path)) == ".p8")
            {
                data = P8PngReadGfxData(path);
            }
            else
            {
                throw new Exception($"Unsupported filename extension: {Path.GetExtension(path)}. Is it a valid pico-8 file?");
            }
        }

        static byte[] P8ReadGfxData(string path)
        {
            string[] lines = File.ReadAllLines(path);
            string[] gfxLines = GfxLinesFromP8Lines(lines);
            return GfxDataFromLines(gfxLines);
        }

        static string[] GfxLinesFromP8Lines(string[] lines)
        {
            int gfxDataStart = Array.IndexOf(lines, "__gfx__");

            Trace.Assert(gfxDataStart != -1, "Couldn't find gfx data section. Is the supplied file valid?");

            return [.. lines.Skip(gfxDataStart + 1).Take(128)];
        }

        static byte[] GfxDataFromLines(string[] lines)
        {
            string gfxString = string.Join("", lines);

            Trace.Assert(gfxString.Length == 128 * 128, $"Incorrect amount of GFX data: {gfxString.Length}");

            byte[] gfxData = new byte[gfxString.Length / 2];

            for (int i = 0; i < gfxData.Length; i++)
            {
                gfxData[i] = (byte)(
                    GfxHexToInt(gfxString[i * 2]) |
                    (GfxHexToInt(gfxString[i * 2 + 1]) << 4)
                );
            }

            return gfxData;
        }

        static int GfxHexToInt(char hex)
        {
            if
            (!(
                hex >= '0' && hex <= '9' ||
                hex >= 'A' && hex <= 'F' ||
                hex >= 'a' && hex <= 'f'
            ))
            {
                return 0; // See p8 file format
            }

            int val = hex;

            return val - (val < 'A' ? '0' : (val < 'a' ? ('A' - 10) : ('a' - 10)));
        }

        static byte[] P8PngReadGfxData(string path)
        {
            SKBitmap bitmap = SKBitmap.Decode(path);

            Trace.Assert(bitmap.Width == 160 && bitmap.Height == 205, $"A .p8.png image should be 160 x 205");

            SKColor[] pixels = bitmap.Pixels;
            byte[] compressedData = new byte[160 * 205];

            for (int i = 0; i < compressedData.Length; i++)
            {
                compressedData[i] = (byte)(
                    (pixels[i].Red & 0b11) << 6 |
                    (pixels[i].Green & 0b11) << 4 |
                    (pixels[i].Blue & 0b11) << 2 |
                    (pixels[i].Alpha & 0b11) << 0
                );
            }

            return [.. compressedData.Take(0x2000)];
        }

        public SKBitmap ToBitmap()
        {
            SKBitmap bitmap = new(128, 128, SKColorType.Rgb888x, SKAlphaType.Opaque);

            for (int y = 0; y < 128; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    int colorVal;
                    if (x % 2 == 0)
                    {
                        colorVal = data[(y * 128 + x) / 2] & 0xF;
                    }
                    else
                    {
                        colorVal = (data[(y * 128 + x - 1) / 2] >> 4) & 0xF;
                    }

                    bitmap.SetPixel(x, y, new P8Color(colorVal).Color);
                }
            }

            return bitmap;
        }
    }

    class P8Color
    {
        public readonly SKColor Color;

        public P8Color(int val)
        {
            Color = val switch
            {
                1 => SKColor.Parse("1D2B53"),
                2 => SKColor.Parse("7E2553"),
                3 => SKColor.Parse("008751"),
                4 => SKColor.Parse("AB5236"),
                5 => SKColor.Parse("5F574F"),
                6 => SKColor.Parse("C2C3C7"),
                7 => SKColor.Parse("FFF1E8"),
                8 => SKColor.Parse("FF004D"),
                9 => SKColor.Parse("FFA300"),
                10 => SKColor.Parse("FFEC27"),
                11 => SKColor.Parse("00E436"),
                12 => SKColor.Parse("29ADFF"),
                13 => SKColor.Parse("83769C"),
                14 => SKColor.Parse("FF77A8"),
                15 => SKColor.Parse("FFCCAA"),
                _ => SKColor.Parse("000000"),
            };
        }
    }
}
