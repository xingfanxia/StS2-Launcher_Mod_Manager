using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace STS2Mobile.Platform;

// Makes a Steam game pack loadable by the stock Godot iOS runtime, in place:
//   1. Sentry has no iOS build: drop its extension and disable its autoload.
//   2. The game ships no .cs resources; stock Godot needs one (any content) at
//      each script path that scenes reference, so one-byte placeholders are added.
// Only the directory at the end of the pack is rewritten. Running it again on a
// prepared pack changes nothing. Any unexpected layout throws, so a pack that
// cannot be prepared is never activated.
public static class IosGamePack
{
    private sealed class Entry
    {
        public byte[] Path; // padded as stored
        public ulong Offset; // as stored
        public ulong Size;
        public byte[] Md5;
        public uint Flags;
        public string Name => Encoding.UTF8.GetString(Path).TrimEnd('\0');
    }

    private const int DirectoryOffsetField = 8 + 12 + 4 + 8;
    private static readonly byte[] Placeholder = { (byte)'\n' };

    public static void Prepare(string packPath, string gameAssemblyPath, Action<string> log)
    {
        var scripts = ScriptPaths(gameAssemblyPath);
        if (scripts.Count == 0)
            throw new InvalidDataException("No script paths found in the game assembly");

        using var file = new FileStream(packPath, FileMode.Open, FileAccess.ReadWrite);
        using var reader = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt32() != 0x43504447)
            throw new InvalidDataException("Not a Godot pack");
        var format = reader.ReadUInt32();
        if (format != 3)
            throw new InvalidDataException($"Unsupported pack format {format}");
        file.Seek(12, SeekOrigin.Current);
        var flags = reader.ReadUInt32();
        if ((flags & 1) != 0)
            throw new InvalidDataException("Encrypted packs are unsupported");
        var filesBase = reader.ReadUInt64();
        var directoryOffset = reader.ReadUInt64();
        var relative = (flags & 2) != 0;

        file.Position = (long)directoryOffset;
        var count = reader.ReadUInt32();
        var entries = new List<Entry>((int)count);
        for (uint i = 0; i < count; i++)
        {
            var entry = new Entry { Path = reader.ReadBytes((int)reader.ReadUInt32()) };
            entry.Offset = reader.ReadUInt64();
            entry.Size = reader.ReadUInt64();
            entry.Md5 = reader.ReadBytes(16);
            entry.Flags = reader.ReadUInt32();
            entries.Add(entry);
        }
        if (file.Position != file.Length)
            throw new InvalidDataException("The pack directory is not the last section");

        long Absolute(Entry entry) => (long)(relative ? entry.Offset + filesBase : entry.Offset);

        var changed = RemoveSentry(file, entries, Absolute, log);

        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
            existing.Add(entry.Name);
        var position = (long)directoryOffset;
        var added = 0;
        var placeholderMd5 = MD5.HashData(Placeholder);
        file.Position = position;
        foreach (var script in scripts)
        {
            if (existing.Contains(script))
                continue;
            position = Align(file, position);
            file.Write(Placeholder);
            var name = Encoding.UTF8.GetBytes(script);
            var padded = new byte[name.Length + (4 - name.Length % 4) % 4];
            name.CopyTo(padded, 0);
            entries.Add(
                new Entry
                {
                    Path = padded,
                    Offset = (ulong)position - (relative ? filesBase : 0),
                    Size = (ulong)Placeholder.Length,
                    Md5 = placeholderMd5,
                }
            );
            position += Placeholder.Length;
            added++;
        }
        if (added == 0 && !changed)
        {
            log("Game pack already prepared for iOS");
            return;
        }

        position = added > 0 ? Align(file, position) : (long)directoryOffset;
        using var directory = new MemoryStream();
        using (var writer = new BinaryWriter(directory, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((uint)entries.Count);
            foreach (var entry in entries)
            {
                writer.Write((uint)entry.Path.Length);
                writer.Write(entry.Path);
                writer.Write(entry.Offset);
                writer.Write(entry.Size);
                writer.Write(entry.Md5);
                writer.Write(entry.Flags);
            }
        }
        file.Position = position;
        directory.WriteTo(file);
        file.SetLength(position + directory.Length);
        file.Position = DirectoryOffsetField;
        file.Write(BitConverter.GetBytes((ulong)position));
        file.Flush(flushToDisk: true);
        log($"Prepared game pack for iOS: {added} script placeholders, {entries.Count} entries");
    }

    private static bool RemoveSentry(
        FileStream file,
        List<Entry> entries,
        Func<Entry, long> absolute,
        Action<string> log
    )
    {
        var sentry = entries.FindIndex(e => e.Name == "addons/sentry/sentry.gdextension");
        if (sentry < 0)
            return false;
        var extensions = entries.Find(e => e.Name == ".godot/extension_list.cfg");
        var project = entries.Find(e => e.Name == "project.binary");
        if (extensions == null || project == null)
            throw new InvalidDataException("The pack has no extension list or project settings");

        var old = Read(file, absolute(extensions), (int)extensions.Size);
        using var kept = new MemoryStream();
        foreach (var line in Encoding.UTF8.GetString(old).Split('\n'))
        {
            if (line.Trim().Length == 0 || line.Contains("sentry"))
                continue;
            kept.Write(Encoding.UTF8.GetBytes(line + "\n"));
        }
        var list = kept.ToArray();
        file.Position = absolute(extensions);
        file.Write(list);
        extensions.Size = (ulong)list.Length;
        extensions.Md5 = MD5.HashData(list);

        // Same-length rename, so the autoload no longer runs and nothing moves.
        var settings = Read(file, absolute(project), (int)project.Size);
        var key = settings.AsSpan().IndexOf(Encoding.ASCII.GetBytes("autoload/SentryInit"));
        if (key >= 0)
        {
            settings[key] = (byte)'x';
            file.Position = absolute(project);
            file.Write(settings);
            project.Md5 = MD5.HashData(settings);
        }
        entries.RemoveAt(sentry);
        log("Removed Sentry from the game pack");
        return true;
    }

    private static byte[] Read(FileStream file, long offset, int size)
    {
        var data = new byte[size];
        file.Position = offset;
        file.ReadExactly(data);
        return data;
    }

    private static long Align(FileStream file, long position)
    {
        var padding = (int)((8 - position % 8) % 8);
        file.Position = position;
        file.Write(new byte[padding]);
        return position + padding;
    }

    // Script resource paths recorded in the game assembly's ScriptPath attributes.
    private static List<string> ScriptPaths(string assemblyPath)
    {
        var text = Encoding.Latin1.GetString(File.ReadAllBytes(assemblyPath));
        var unique = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(text, @"res://[A-Za-z0-9_/.]+\.cs"))
            unique.Add(match.Value);
        return new List<string>(unique);
    }
}
