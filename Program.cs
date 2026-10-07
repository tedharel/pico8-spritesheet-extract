using System.Diagnostics;
using SkiaSharp;

namespace P8Extract;

class Program
{
    static int Main(string[] args)
    {
        try
        {
            Config config = new(args);

            switch (config.Command)
            {
                case Config.ExtractCommand.SpriteSheet:
                    Spritesheet(config);
                    break;
                case Config.ExtractCommand.Sprites:
                    Sprites(config);
                    break;
                case Config.ExtractCommand.Map:
                    Map(config);
                    break;
            }
        }
        catch (P8ExtractException exception)
        {
            const string red = "\u001b[31m";
            const string reset = "\u001b[0m";

            Console.WriteLine($"""
            {red}Error: {exception.Message}{reset}

            Usage: p8-extract <command> [options]

            Commands:
                spritesheet <input_file> <output_file.png>
                sprites <input_file> <output_folder>
                map <input_file> <output_file.csv>
            
            Options:
                -i, --ignore-shared
                            Don't include the shared map/spritesheet data
                -t, --transparent-background
                            Replace black pixels in the spritesheet with transparent ones,
                            similar to how spr() works by default
                -f, --flatten-map
                            Outputs map data as one long list, instead of a 2d list
            """);

            return 1;
        }

        return 0;
    }

    static void Spritesheet(Config config)
    {
        P8GfxData gfxData = new(config.InputPath, config.InputExtension);
        SKBitmap bitmap = gfxData.ToBitmap(config.Options);

        Trace.Assert(config.OutputPath != null, "Output path should be set by Config()");

        try
        {
            using (FileStream stream = new(config.OutputPath, FileMode.Create, FileAccess.Write))
            {
                bool success = bitmap.Encode(stream, SKEncodedImageFormat.Png, 0);

                if (!success)
                {
                    throw new P8ExtractException("failed to encode spritesheet");
                }
            };
        }
        catch (Exception exception)
        {
            if (exception is P8ExtractException)
            {
                throw;
            }
            else
            {
                throw new P8ExtractException($"failed to save spritesheet to '{config.OutputPath}', caused by: {exception.Message}");
            }
        }
    }

    static void Sprites(Config config)
    {
        P8GfxData gfxData = new(config.InputPath, config.InputExtension);
        SKBitmap bitmap = gfxData.ToBitmap(config.Options);

        Trace.Assert(config.OutputPath != null, "Output path should be set by Config()");

        SKBitmap sprite = new(8, 8, bitmap.ColorType, bitmap.AlphaType);

        int spritesCount = config.Options.IgnoreShared ? 128 : 256;

        for (int i = 0; i < spritesCount; i++)
        {
            int y = (i / 16) * 8;
            int x = (i % 16) * 8;

            {
                bool success = bitmap.ExtractSubset(sprite, new SKRectI(x, y, x + 8, y + 8));

                if (!success)
                {
                    throw new P8ExtractException($"failed to extract sprite {i} from spritesheet");
                }
            }

            string path = Path.Join(config.OutputPath, $"{i}.png");

            try
            {
                using (FileStream stream = new(path, FileMode.Create, FileAccess.Write))
                {
                    bool success = sprite.Encode(stream, SKEncodedImageFormat.Png, 0);

                    if (!success)
                    {
                        throw new P8ExtractException($"failed to encode sprite {i}");
                    }
                };
            }
            catch (Exception exception)
            {
                if (exception is P8ExtractException)
                {
                    throw;
                }
                else
                {
                    throw new P8ExtractException($"failed to save sprite {i} to '{path}', caused by: {exception.Message}");
                }
            }
        }

    }

    static void Map(Config config)
    {
        P8MapData mapData = new(config.InputPath, config.InputExtension);

        Trace.Assert(config.OutputPath != null, "Output path should be set by Config()");
        
        int length = config.Options.IgnoreShared ? mapData.Data.Length / 2 : mapData.Data.Length;
        string output;

        if (config.Options.FlattenMap)
        {
            output = string.Join("\n", mapData.Data.Take(length));
        }
        else
        {
            string[] lines = [.. mapData.Data.Take(length).Chunk(128).Select(line => string.Join("\t", line))];
            output = string.Join("\n", lines);
        }
        
        try
        {
            File.WriteAllText(config.OutputPath, output);
        }
        catch (Exception exception)
        {
            throw new P8ExtractException($"failed to save map to '{config.OutputPath}', caused by: {exception.Message}");
        }
    }
}

class Config
{
    public readonly string InputPath;
    public readonly P8Extension InputExtension;
    public readonly ExtractCommand Command;

    public readonly string? OutputPath;
    public readonly Options Options;

    public Config(string[] args)
    {
        (args, Options) = ParseAndRemoveOptions(args);

        if (args.Length < 1)
        {
            throw new P8ExtractException("no arguments specified");
        }

        Command = ParseCommand(args[0]);

        if (args.Length < 2)
        {
            throw new P8ExtractException("input file not specified");
        }

        (InputPath, InputExtension) = ParseInputFilePath(args[1]);

        string[] remaining = args[2..];

        switch (Command)
        {
            case ExtractCommand.SpriteSheet:
                OutputPath = ParseSpritesheetArguments(remaining);
                break;
            case ExtractCommand.Sprites:
                OutputPath = ParseSpritesArguments(remaining);
                break;
            case ExtractCommand.Map:
                OutputPath = ParseMapArguments(remaining);
                break;
        }
    }

    static (string[], Options) ParseAndRemoveOptions(string[] args)
    {
        Options options = new();
        List<string> normalArgs = [];

        foreach (string argument in args)
        {
            if (!argument.StartsWith('-'))
            {
                normalArgs.Add(argument);
                continue;
            }

            if (argument.StartsWith("--"))
            {
                options.ParseLongOption(argument);
            }
            else
            {
                options.ParseShortOptions(argument);
            }
        }

        return ([.. normalArgs], options);
    }

    static (string, P8Extension) ParseInputFilePath(string path)
    {
        try
        {
            FileInfo _ = new(path);
        }
        catch (Exception exception)
        {
            throw new P8ExtractException($"invalid input file path, caused by: {exception.Message}");
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
            throw new P8ExtractException($"invalid input file extension '{Path.GetExtension(path)}'. This should be either '.p8' or '.p8.png'");
        }

        return (path, extension);
    }

    static ExtractCommand ParseCommand(string command)
    {
        return command.ToLower() switch
        {
            "spritesheet" => ExtractCommand.SpriteSheet,
            "sprites" => ExtractCommand.Sprites,
            "map" => ExtractCommand.Map,
            _ => throw new P8ExtractException($"invalid command '{command}'"),
        };
    }

    static string ParseSpritesheetArguments(string[] args)
    {
        if (args.Length < 1)
        {
            throw new P8ExtractException("<output_file.png> not specified");
        }

        try
        {
            FileInfo _ = new(args[0]);
        }
        catch (Exception exception)
        {
            throw new P8ExtractException($"invalid output file path, caused by: {exception.Message}");
        }

        if (Path.GetExtension(args[0]) != ".png")
        {
            throw new P8ExtractException($"invalid output file extension '{Path.GetExtension(args[0])}'. This should be '.png'");
        }

        return args[0];
    }

    static string ParseSpritesArguments(string[] args)
    {
        if (args.Length < 1)
        {
            throw new P8ExtractException("<output_folder> not specified");
        }

        try
        {
            FileInfo _ = new(args[0]);
        }
        catch (Exception exception)
        {
            throw new P8ExtractException($"invalid output folder path, caused by: {exception.Message}");
        }

        if (!Path.Exists(args[0]))
        {
            throw new P8ExtractException($"couldn't find output folder '{args[0]}'");
        }

        return args[0];
    }

    static string ParseMapArguments(string[] args)
    {
        if (args.Length < 1)
        {
            throw new P8ExtractException("<output_file.csv> not specified");
        }

        try
        {
            FileInfo _ = new(args[0]);
        }
        catch (Exception exception)
        {
            throw new P8ExtractException($"invalid output file path, caused by: {exception.Message}");
        }

        if (Path.GetExtension(args[0]) != ".csv")
        {
            throw new P8ExtractException($"invalid output file extension '{Path.GetExtension(args[0])}'. This should be '.csv'");
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
        Sprites,
        Map,
    }
}

struct Options
{
    public bool IgnoreShared = false;
    public bool TransparentBackground = false;
    public bool FlattenMap = false;

    public Options()
    {
    }

    public void ParseLongOption(string longOption)
    {
        switch (longOption)
        {
            case "--ignore-shared":
                IgnoreShared = true;
                break;
            case "--transparent-background":
                TransparentBackground = true;
                break;
            case "--flatten-map":
                FlattenMap = true;
                break;
            default:
                throw new P8ExtractException($"unknown option '{longOption}'");
        }
    }

    public void ParseShortOptions(string shortOptions)
    {
        shortOptions = shortOptions[1..];

        foreach (char flag in shortOptions)
        {
            switch (flag)
            {
                case 'i':
                    IgnoreShared = true;
                    break;
                case 't':
                    TransparentBackground = true;
                    break;
                case 'f':
                    FlattenMap = true;
                    break;
                default:
                    throw new P8ExtractException($"unknown option '-{flag}'");
            }
        }
    }
}

class P8Data
{
    public static string[] P8GetSection(string path, string label, int length)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception exception)
        {
            throw new P8ExtractException($"failed to read input file '{path}', caused by: {exception.Message}");
        }
        
        int dataStart = Array.IndexOf(lines, label);

        if (dataStart == -1)
        {
            throw new P8ExtractException($"couldn't find section '{label}'. Is the supplied file valid?");
        }

        return [.. lines.Skip(dataStart + 1).Take(length)];
    }

    public static byte[] P8PngGetCompressedData(string path)
    {
        SKFileStream stream = new(path);
        SKCodec codec = SKCodec.Create(stream) ?? throw new P8ExtractException($"failed to read input file '{path}'");
        SKBitmap bitmap = new(codec.Info);
        SKCodecResult result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());

        if (result != SKCodecResult.Success)
        {
            throw new P8ExtractException($"failed to read input file '{path}'. Failed with error '{result}'");
        }

        if (!(bitmap.Width == 160 && bitmap.Height == 205))
        {
            throw new P8ExtractException($"incorrect image dimensions ({bitmap.Width} x {bitmap.Height}). A p8.png image should be 160 x 205. Is the supplied file valid?");
        }

        SKColor[] pixels = bitmap.Pixels;
        byte[] compressedData = new byte[160 * 205];

        for (int i = 0; i < compressedData.Length; i++)
        {
            compressedData[i] = (byte)(
                (pixels[i].Alpha & 0b11) << 6 |
                (pixels[i].Red & 0b11) << 4 |
                (pixels[i].Green & 0b11) << 2 |
                (pixels[i].Blue & 0b11) << 0
            );
        }

        return compressedData;
    }

    public static int HexToInt(char hex)
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

}

class P8GfxData(string path, Config.P8Extension extension) : P8Data
{
    public byte[] Data = extension switch
    {
        Config.P8Extension.P8 => P8ReadGfxData(path),
        Config.P8Extension.P8Png => P8PngReadGfxData(path),
        _ => throw new UnreachableException()
    };

    static byte[] P8ReadGfxData(string path)
    {
        string[] gfxLines = P8GetSection(path, "__gfx__", 128);
        string gfxString = string.Join("", gfxLines);

        if (gfxString.Length != 128 * 128)
        {
            throw new P8ExtractException($"Incorrect amount of GFX data ({gfxString.Length}). Should be {128 * 128}. Is the supplied file valid?");
        }

        byte[] gfxData = new byte[gfxString.Length / 2];

        for (int i = 0; i < gfxData.Length; i++)
        {
            gfxData[i] = (byte)(
                HexToInt(gfxString[i * 2]) |
                (HexToInt(gfxString[i * 2 + 1]) << 4)
            );
        }

        return gfxData;
    }

    static byte[] P8PngReadGfxData(string path)
    {
        byte[] compressedData = P8PngGetCompressedData(path);
        return [.. compressedData.Take(0x2000)];
    }

    public SKBitmap ToBitmap(Options options)
    {
        int height = options.IgnoreShared ? 64 : 128;

        SKBitmap bitmap = new(128, height, SKColorType.Rgba8888, SKAlphaType.Premul);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                int colorVal;
                if (x % 2 == 0)
                {
                    colorVal = Data[(y * 128 + x) / 2] & 0xF;
                }
                else
                {
                    colorVal = (Data[(y * 128 + x - 1) / 2] >> 4) & 0xF;
                }

                if (options.TransparentBackground && colorVal == 0)
                {
                    bitmap.SetPixel(x, y, new SKColor(0, 0, 0, 0));
                    continue;
                }

                bitmap.SetPixel(x, y, new P8Color(colorVal).Color);
            }
        }

        return bitmap;
    }
}

class P8MapData(string path, Config.P8Extension extension) : P8Data
{
    public byte[] Data = extension switch
    {
        Config.P8Extension.P8 => P8ReadMapData(path),
        Config.P8Extension.P8Png => P8PngReadMapData(path),
        _ => throw new UnreachableException()
    };
    
    static byte[] P8ReadMapData(string path)
    {
        string[] topMapLines = P8GetSection(path, "__map__", 32);
        string topMapString = string.Join("", topMapLines);
        
        if (topMapString.Length != 32 * 256)
        {
            throw new P8ExtractException($"Incorrect amount of MAP data ({topMapString.Length}). Should be {32 * 256}. Is the supplied file valid?");
        }
        
        byte[] topMapData = new byte[topMapString.Length / 2];

        for (int i = 0; i < topMapData.Length; i++)
        {
            topMapData[i] = (byte)(
                (HexToInt(topMapString[i * 2]) << 4) |
                HexToInt(topMapString[i * 2 + 1])
            );
        }

        string[] gfxLines = P8GetSection(path, "__gfx__", 128);
        string[] bottomMapLines = [.. gfxLines.Skip(64).Take(64)];
        string bottomMapString = string.Join("", bottomMapLines);
        
        if (bottomMapString.Length != 128 * 64)
        {
            throw new P8ExtractException($"Incorrect amount of shared GFX data ({bottomMapString.Length}). Should be {128 * 64}. Is the supplied file valid?");
        }
        
        byte[] bottomMapData = new byte[bottomMapString.Length / 2];

        for (int i = 0; i < bottomMapData.Length; i++)
        {
            bottomMapData[i] = (byte)(
                HexToInt(bottomMapString[i * 2]) |
                (HexToInt(bottomMapString[i * 2 + 1]) << 4)
            );
        }

        return [.. topMapData, .. bottomMapData];
    }

    static byte[] P8PngReadMapData(string path)
    {
        byte[] compressedData = P8PngGetCompressedData(path);
        return [
            .. compressedData.Skip(0x2000).Take(0x1000),
            .. compressedData.Skip(0x1000).Take(0x1000)
        ];
    }
}

readonly struct P8Color(int val)
{
    public readonly SKColor Color = val switch
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

public class P8ExtractException : Exception
{
    public P8ExtractException()
    {
    }

    public P8ExtractException(string message)
        : base(message)
    {
    }

    public P8ExtractException(string message, Exception inner)
        : base(message, inner)
    {
    }
}