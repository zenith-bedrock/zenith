using System.IO.Compression;
using Zenith.GamedataImport;
using Zenith.Nbt;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: gamedata-import <path-to-recipes.brarchive> <output-recipes.nbt>");
    return 1;
}

var inputPath = args[0];
var outputPath = args[1];

var entries = BrArchiveReader.Read(inputPath);
Console.WriteLine($"Unpacked {entries.Count} entries from {inputPath}.");

var document = VanillaRecipeImporter.BuildDocument(entries);
var encoded = NbtCodec.Encode(document, NbtEncoding.LittleEndian);

using (var fs = File.Create(outputPath))
using (var gz = new GZipStream(fs, CompressionLevel.SmallestSize))
    gz.Write(encoded);

var compressedSize = new FileInfo(outputPath).Length;
Console.WriteLine($"Wrote {outputPath}: {encoded.Length} bytes NBT, {compressedSize} bytes gzip.");
return 0;
