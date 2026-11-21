using System.IO.Compression;
using System.Text;

namespace ESPLanguageEN;

// Skyrim SE plugin (.esp / .esm / .esl) as a tree of GRUPs and records.
// Records that were not edited are written back byte for byte; edited records are rebuilt
// (and recompressed when the record was compressed). GRUP sizes are recalculated on save.

public sealed class Sub
{
    public string Type;
    public byte[] Data;
}

public abstract class Node
{
    public abstract void Write(Stream s);
}

public sealed class GroupNode : Node
{
    public byte[] Header;                       // 24 bytes
    public List<Node> Children = new();

    public override void Write(Stream s)
    {
        long start = s.Position;
        s.Write(Header);
        foreach (var c in Children) c.Write(s);
        long end = s.Position;
        BitConverter.TryWriteBytes(Header.AsSpan(4, 4), (uint)(end - start));
        s.Position = start + 4;
        s.Write(Header, 4, 4);
        s.Position = end;
    }
}

public sealed class RecordNode : Node
{
    public byte[] Header;                       // 24 bytes
    public byte[] Raw;                          // data as stored in the file (maybe compressed)
    public bool Dirty;
    List<Sub> subs;

    public string Type => Encoding.ASCII.GetString(Header, 0, 4);
    public uint Flags => BitConverter.ToUInt32(Header, 8);
    public uint FormId => BitConverter.ToUInt32(Header, 12);
    public bool Compressed => (Flags & 0x00040000) != 0;

    public List<Sub> Subs => subs ??= ParseSubs(Decompress());

    public byte[] Decompress()
    {
        if (!Compressed) return Raw;
        int size = (int)BitConverter.ToUInt32(Raw, 0);
        var outb = new byte[size];
        using var src = new MemoryStream(Raw, 4, Raw.Length - 4);
        using var z = new ZLibStream(src, CompressionMode.Decompress);
        int read = 0;
        while (read < size)
        {
            int n = z.Read(outb, read, size - read);
            if (n <= 0) throw new InvalidDataException($"Record {FormId:X8}: compressed data is short");
            read += n;
        }
        return outb;
    }

    static List<Sub> ParseSubs(byte[] d)
    {
        var list = new List<Sub>();
        int p = 0;
        uint big = 0;
        while (p + 6 <= d.Length)
        {
            string t = Encoding.ASCII.GetString(d, p, 4);
            int sz = BitConverter.ToUInt16(d, p + 4);
            p += 6;
            if (t == "XXXX")                    // next subrecord is bigger than 64 KB
            {
                big = BitConverter.ToUInt32(d, p);
                p += sz;
                continue;
            }
            int len = big != 0 ? (int)big : sz;
            big = 0;
            if (p + len > d.Length) throw new InvalidDataException($"Subrecord {t} runs past the record end");
            list.Add(new Sub { Type = t, Data = d.AsSpan(p, len).ToArray() });
            p += len;
        }
        return list;
    }

    public static byte[] BuildSubs(List<Sub> subs)
    {
        using var ms = new MemoryStream();
        foreach (var s in subs)
        {
            if (s.Data.Length > 0xFFFF)
            {
                ms.Write(Encoding.ASCII.GetBytes("XXXX"));
                ms.Write(BitConverter.GetBytes((ushort)4));
                ms.Write(BitConverter.GetBytes((uint)s.Data.Length));
                ms.Write(Encoding.ASCII.GetBytes(s.Type));
                ms.Write(BitConverter.GetBytes((ushort)0));
            }
            else
            {
                ms.Write(Encoding.ASCII.GetBytes(s.Type));
                ms.Write(BitConverter.GetBytes((ushort)s.Data.Length));
            }
            ms.Write(s.Data);
        }
        return ms.ToArray();
    }

    static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes((uint)data.Length));
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(data);
        return ms.ToArray();
    }

    public override void Write(Stream s)
    {
        if (Dirty)
        {
            byte[] data = BuildSubs(Subs);
            Raw = Compressed ? Compress(data) : data;
            BitConverter.TryWriteBytes(Header.AsSpan(4, 4), (uint)Raw.Length);
            Dirty = false;
        }
        s.Write(Header);
        s.Write(Raw);
    }
}

public sealed class Plugin
{
    public string Path;
    public List<Node> Top = new();
    public RecordNode Header;

    public bool Localized => (Header.Flags & 0x80) != 0;

    public static Plugin Load(string path)
    {
        var b = File.ReadAllBytes(path);
        var p = new Plugin { Path = path };
        int pos = 0;
        p.Top = ParseList(b, ref pos, b.Length);
        p.Header = p.Top.FirstOrDefault() as RecordNode;
        if (p.Header == null || p.Header.Type != "TES4")
            throw new InvalidDataException("Not a Skyrim plugin (no TES4 header).");
        return p;
    }

    static List<Node> ParseList(byte[] b, ref int pos, int end)
    {
        var list = new List<Node>();
        while (pos + 24 <= end)
        {
            string t = Encoding.ASCII.GetString(b, pos, 4);
            int size = (int)BitConverter.ToUInt32(b, pos + 4);
            var h = b.AsSpan(pos, 24).ToArray();
            if (t == "GRUP")
            {
                int gEnd = pos + size;
                if (size < 24 || gEnd > end) throw new InvalidDataException($"Bad GRUP size at 0x{pos:X}");
                var g = new GroupNode { Header = h };
                pos += 24;
                g.Children = ParseList(b, ref pos, gEnd);
                list.Add(g);
            }
            else
            {
                if (pos + 24 + size > end) throw new InvalidDataException($"Bad {t} record size at 0x{pos:X}");
                list.Add(new RecordNode { Header = h, Raw = b.AsSpan(pos + 24, size).ToArray() });
                pos += 24 + size;
            }
        }
        if (pos != end) throw new InvalidDataException($"Unexpected data at 0x{pos:X} (this is not a Skyrim SE plugin?)");
        return list;
    }

    public IEnumerable<RecordNode> Records()
    {
        var stack = new Stack<IEnumerator<Node>>();
        stack.Push(Top.GetEnumerator());
        while (stack.Count > 0)
        {
            var it = stack.Peek();
            if (!it.MoveNext()) { stack.Pop(); continue; }
            if (it.Current is RecordNode r) yield return r;
            else if (it.Current is GroupNode g) stack.Push(g.Children.GetEnumerator());
        }
    }

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        foreach (var n in Top) n.Write(ms);
        return ms.ToArray();
    }
}
