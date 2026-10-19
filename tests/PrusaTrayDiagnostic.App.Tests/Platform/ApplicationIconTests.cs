using System.Buffers.Binary;
using System.Reflection.PortableExecutable;

namespace PrusaTrayDiagnostic.App.Tests.Platform;

/// <summary>
/// <c>ApplicationIcon</c> (T-025): the icon is a Win32 resource of the built assembly, which the SDK copies into the exe
/// (apphost) and Native AOT into the published binary. Reads the PE resource directory, so it runs on every OS.
/// </summary>
public sealed class ApplicationIconTests
{
    private const int GroupIconType = 14;
    private const int IconType = 3;

    [Fact]
    public void AssemblyCarriesTheAppIconWithEverySize()
    {
        string assembly = typeof(App).Assembly.Location;

        Dictionary<int, List<byte[]>> resources = ReadWin32Resources(assembly);

        List<byte[]> groups = Assert.Contains(GroupIconType, resources);
        byte[] group = Assert.Single(groups);
        int[] sizes = GroupIconSizes(group);
        Assert.Equal([256, 64, 48, 32, 24, 20, 16], sizes);
        Assert.Equal(sizes.Length, resources[IconType].Count);
    }

    /// <summary>GRPICONDIR: reserved, type 1, count, then 14-byte entries starting with width and height.</summary>
    private static int[] GroupIconSizes(byte[] group)
    {
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(2)));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(4));
        int[] sizes = new int[count];
        for (int i = 0; i < count; i++)
        {
            byte width = group[6 + (14 * i)];
            sizes[i] = width == 0 ? 256 : width;
        }

        return sizes;
    }

    /// <summary>Resource data per numeric type (all names and languages).</summary>
    private static Dictionary<int, List<byte[]>> ReadWin32Resources(string path)
    {
        using FileStream file = File.OpenRead(path);
        using PEReader reader = new(file);
        DirectoryEntry table = reader.PEHeaders.PEHeader!.ResourceTableDirectory;
        Assert.True(table.Size > 0, $"{Path.GetFileName(path)} has no Win32 resources.");
        byte[] section = [.. reader.GetSectionData(table.RelativeVirtualAddress).GetContent(0, table.Size)];

        Dictionary<int, List<byte[]>> result = [];
        foreach ((int type, int typeOffset) in Entries(section, 0))
        {
            List<byte[]> data = [];
            foreach ((_, int nameOffset) in Entries(section, typeOffset))
            {
                foreach ((_, int languageOffset) in Entries(section, nameOffset))
                {
                    int rva = BinaryPrimitives.ReadInt32LittleEndian(section.AsSpan(languageOffset));
                    int size = BinaryPrimitives.ReadInt32LittleEndian(section.AsSpan(languageOffset + 4));
                    data.Add([.. reader.GetSectionData(rva).GetContent(0, size)]);
                }
            }

            result[type] = data;
        }

        return result;
    }

    /// <summary>IMAGE_RESOURCE_DIRECTORY entries: id (names reported as -1) and the offset of the subdirectory or data entry.</summary>
    private static IEnumerable<(int Id, int Offset)> Entries(byte[] section, int directory)
    {
        int count = BinaryPrimitives.ReadUInt16LittleEndian(section.AsSpan(directory + 12))
            + BinaryPrimitives.ReadUInt16LittleEndian(section.AsSpan(directory + 14));
        for (int i = 0; i < count; i++)
        {
            int entry = directory + 16 + (8 * i);
            uint name = BinaryPrimitives.ReadUInt32LittleEndian(section.AsSpan(entry));
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(section.AsSpan(entry + 4));
            yield return ((name & 0x8000_0000) != 0 ? -1 : (int)name, (int)(offset & 0x7FFF_FFFF));
        }
    }
}
