using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace P8SpritesheetExtract {
    class Program {
        static void Main(string[] args) {
            Config config;

            try
            {
                config = new Config(args);
            }
            catch (Exception exception)
            {
                string message = $"""
                Error parsing arguments: {exception.Message}
                Usage: P8SpritesheetExtract [options] <file> ...
                    -h, --help    show this menu
                """;

                Console.WriteLine(message);
                Environment.Exit(1);

                // This shouldn't trigger, but it makes the compiler happy
                throw new Exception(message);
            }

            P8GfxData gfxData;

            try
            {
                gfxData = new(config.path);
            }
            catch (Exception exception)
            {
                string message = $"""
                Error reading spritesheet data: {exception.Message}
                Usage: P8SpritesheetExtract [options] <file> ...
                    -h, --help    show this menu
                """;

                Console.WriteLine(message);
                Environment.Exit(1);

                // This shouldn't trigger, but it makes the compiler happy
                throw new Exception(message);
            }

            Image<Argb32> image;

            try
            {
                image = gfxData.ToImage();
            }
            catch (Exception exception)
            {
                string message = $"""
                Error converting spritesheet data: {exception.Message}
                Usage: P8SpritesheetExtract [options] <file> ...
                    -h, --help    show this menu
                """;

                Console.WriteLine(message);
                Environment.Exit(1);

                // This shouldn't trigger, but it makes the compiler happy
                throw new Exception(message);
            }

            image.SaveAsPng($"{config.name}.png");
        }
    }

    class Config {
        public readonly string path;
        public readonly string name;

        public Config(string[] args) {
            if (args.Contains("-h") || args.Contains("--help"))
            {
                Console.WriteLine("""
                Usage: P8SpritesheetExtract [options] <file> ...
                    -h, --help    show this menu
                """);
                Environment.Exit(0);
            }

            if (args.Length < 1)
            {
                throw new Exception("not enough arguments");
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
                ) {
                    throw new Exception("invalid file path");
                } else {
                    throw;
                }
            }

            path = args[0];
            
            if (Path.GetExtension(path) == ".png" && Path.GetExtension(Path.GetFileNameWithoutExtension(path)) == ".p8")
            {
                name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));
            }
            else
            {
                name = Path.GetFileNameWithoutExtension(path);
            }
        }
    }

    class P8GfxData {
        public byte[] data;

        public P8GfxData(string path) {
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
                throw new Exception($"Unsupported filename extension ({Path.GetExtension(path)}). Is it a valid pico-8 file?");
            }
        }

        static byte[] P8ReadGfxData(string path) {
            string[] lines = File.ReadAllLines(path);
            string[] gfxLines = GfxLinesFromP8Lines(lines);
            return GfxDataFromLines(gfxLines);
        }

        static string[] GfxLinesFromP8Lines(string[] lines) {
            int gfxDataStart = Array.IndexOf(lines, "__gfx__");

            Trace.Assert(gfxDataStart != -1, "Couldn't find gfx data section. Is the supplied file valid?");

            return lines.Skip(gfxDataStart + 1).Take(128).ToArray();
        }

        static byte[] GfxDataFromLines(string[] lines) {
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

        static int GfxHexToInt(char hex) {
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

        static byte[] P8PngReadGfxData(string path) {
            Image<Argb32> image = Image.Load(path).CloneAs<Argb32>();

            Trace.Assert(image.Bounds == new Rectangle(0 ,0, 160, 205), $"A .p8.png image should be 160 x 205");

            byte[] bytes = new byte[160 * 205 * 4];
            image.CopyPixelDataTo(bytes);

            byte[] compressedData = new byte[160 * 205];
            for (int i = 0; i < compressedData.Length; i++)
            {
                compressedData[i] = (byte)(
                    (bytes[i * 4 + 0] & 0b11) << 6 |
                    (bytes[i * 4 + 1] & 0b11) << 4 |
                    (bytes[i * 4 + 2] & 0b11) << 2 |
                    (bytes[i * 4 + 3] & 0b11) << 0
                );
            }

            return compressedData.Take(0x2000).ToArray();
        }

        public Image<Argb32> ToImage() {
            Image<Argb32> image = new(128, 128);

            for (int y = 0; y < 128; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    int colorVal;
                    if (x % 2 == 0) {
                        colorVal = this.data[(y * 128 + x) / 2] & 0xF;
                    } else {
                        colorVal = (this.data[(y * 128 + x - 1) / 2] >> 4) & 0xF;
                    }

                    image[x, y] = new P8Color(colorVal).Color;
                }
            }

            return image;
        }
    }

    class P8Color {
        public readonly Color Color;

        public P8Color(int val) {
            Color = val switch
            {
                1 => Color.ParseHex("1D2B53"),
                2 => Color.ParseHex("7E2553"),
                3 => Color.ParseHex("008751"),
                4 => Color.ParseHex("AB5236"),
                5 => Color.ParseHex("5F574F"),
                6 => Color.ParseHex("C2C3C7"),
                7 => Color.ParseHex("FFF1E8"),
                8 => Color.ParseHex("FF004D"),
                9 => Color.ParseHex("FFA300"),
                10 => Color.ParseHex("FFEC27"),
                11 => Color.ParseHex("00E436"),
                12 => Color.ParseHex("29ADFF"),
                13 => Color.ParseHex("83769C"),
                14 => Color.ParseHex("FF77A8"), 
                15 => Color.ParseHex("FFCCAA"),
                _ => Color.ParseHex("000000"),
            };
        }
    }
}
