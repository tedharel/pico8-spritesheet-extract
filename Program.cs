using System.Diagnostics;
using SkiaSharp;

namespace P8Extract;

class Program
{
    static void Main(string[] args)
    {
        try
        {
            Config config = new(args);

            switch (config.command)
            {
                case Config.ExtractCommand.SpriteSheet:
                    P8GfxData gfxData = new(config.input_path, config.input_extension);
                    SKBitmap bitmap = gfxData.ToBitmap();

                    Trace.Assert(config.output_path != null, "Output path should be set by Config()");

                    using (FileStream stream = new(config.output_path, FileMode.Create, FileAccess.Write))
                    {
                        bitmap.Encode(stream, SKEncodedImageFormat.Png, 0);
                    };
                    break;
            }
        }
        catch (Exception exception)
        {
            const string red = "\u001b[31m";
            const string reset = "\u001b[0m";

            Console.WriteLine($"""
            {red}Error: {exception.Message}{reset}

            usage: p8-extract <input_file> <command> [options]

            commands:
                - spritesheet <output_file.png>
            """);
        }
    }
}

class Config
{
    public readonly string input_path;
    public readonly P8Extension input_extension;
    public readonly ExtractCommand command;

    public readonly string? output_path;

    public Config(string[] args)
    {
        if (args.Length < 1)
        {
            throw new Exception("no arguments specified");
        }

        (input_path, input_extension) = ParseInputFilePath(args[0]);

        if (args.Length < 2)
        {
            throw new Exception("command not specified");
        }

        command = ParseCommand(args[1]);

        string[] remaining = args[2..];

        switch (command)
        {
            case ExtractCommand.SpriteSheet:
                output_path = ParseSpritesheetArguments(remaining);
                break;
        }
    }

    static (string, P8Extension) ParseInputFilePath(string path)
    {
        try
        {
            FileInfo _ = new(path);
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

        P8Extension extension;

        if (Path.GetExtension(path) == ".p8")
        {
            extension = P8Extension.P8;
        }
        else if (Path.GetExtension(path) == ".png" && Path.GetExtension(Path.GetFileNameWithoutExtension(path)) == ".p8")
        {
            extension = P8Extension.P8Png;
        }
        else
        {
            throw new Exception($"invalid input file extension '{Path.GetExtension(path)}'. This should be either '.p8' or '.p8.png'");
        }

        return (path, extension);
    }

    static ExtractCommand ParseCommand(string command)
    {
        return command.ToLower() switch
        {
            "spritesheet" => ExtractCommand.SpriteSheet,
            _ => throw new Exception($"invalid command '{command}'"),
        };
    }

    static string ParseSpritesheetArguments(string[] args)
    {
        if (args.Length < 1)
        {
            throw new Exception("<output_file.png> not specified");
        }

        if (Path.GetExtension(args[0]) != ".png")
        {
            throw new Exception($"invalid output file extension '{Path.GetExtension(args[0])}'. This should be '.png'");
        }

        return args[0];
    }

    public enum P8Extension
    {
        P8,
        P8Png,
    }

    public enum ExtractCommand
    {
        SpriteSheet,
    }
}

class P8GfxData
{
    public byte[] data;

    public P8GfxData(string path, Config.P8Extension extension)
    {
        data = extension switch
        {
            Config.P8Extension.P8 => P8ReadGfxData(path),
            Config.P8Extension.P8Png => P8PngReadGfxData(path),
            _ => throw new UnreachableException()
        };
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

        if (gfxDataStart == -1)
        {
            throw new Exception("couldn't find gfx data section. Is the supplied file valid?");
        }

        return [.. lines.Skip(gfxDataStart + 1).Take(128)];
    }

    static byte[] GfxDataFromLines(string[] lines)
    {
        string gfxString = string.Join("", lines);

        if (gfxString.Length != 128 * 128)
        {
            throw new Exception($"Incorrect amount of GFX data ({gfxString.Length}). Should be {128 * 128}. Is the supplied file valid?");
        }

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

        if (!(bitmap.Width == 160 && bitmap.Height == 205))
        {
            throw new Exception($"incorrect image dimensions ({bitmap.Width} x {bitmap.Height}). A p8.png image should be 160 x 205. Is the supplied file valid?");
        }

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
