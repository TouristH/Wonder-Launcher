// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Wonder.Core;

namespace Wonder.Infrastructure;

internal static class InstanceWorldMetadata
{
    private const string Icon = "icon.png";

    internal static InstanceContentMetadata Read(Func<string, Stream?> open)
    {
        try
        {
            using var source = open("level.dat");
            if (source is not null)
            {
                using var decoded = new BufferedStream(new GZipStream(source, CompressionMode.Decompress, leaveOpen: true));
                var reader = new NbtReader(decoded);
                if (reader.ReadTag() == Tag.Compound)
                {
                    reader.SkipString();
                    while (reader.ReadTag() is var type && type != Tag.End)
                    {
                        if (reader.ReadString() == "Data" && type == Tag.Compound)
                            return ReadData(reader);
                        reader.Skip(type);
                    }
                }
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // A damaged level.dat does not prevent listing the save or reading its preview.
        }
        return new(IconPath: Icon);
    }

    private static InstanceContentMetadata ReadData(NbtReader reader)
    {
        string? title = null, version = null;
        int? dataVersion = null, versionId = null, gameMode = null;
        string? difficulty = null;
        bool? hardcore = null, commands = null;
        long? lastPlayed = null;
        while (reader.ReadTag() is var type && type != Tag.End)
        {
            switch ((reader.ReadString(), type))
            {
                case ("LevelName", Tag.String): title = DisplayText(reader.ReadString()); break;
                case ("Version", Tag.Compound): (version, versionId) = ReadVersion(reader); break;
                case ("DataVersion", Tag.Int): dataVersion = reader.ReadInt32(); break;
                case ("GameType", Tag.Int): gameMode = reader.ReadInt32(); break;
                case ("Difficulty", Tag.Byte):
                    difficulty = reader.ReadByte() switch { 0 => "和平", 1 => "简单", 2 => "普通", 3 => "困难", _ => null };
                    break;
                case ("difficulty_settings", Tag.Compound): difficulty = ReadDifficulty(reader); break;
                case ("hardcore", Tag.Byte): hardcore = reader.ReadByte() != 0; break;
                case ("allowCommands", Tag.Byte): commands = reader.ReadByte() != 0; break;
                case ("LastPlayed", Tag.Long): lastPlayed = reader.ReadInt64(); break;
                default: reader.Skip(type); break;
            }
        }

        var details = new List<InstanceContentDetail>();
        if (version is not null) details.Add(new("Minecraft 版本", version));
        if ((dataVersion ?? versionId) is int number && number > 0)
            details.Add(new("数据版本", number.ToString(CultureInfo.InvariantCulture)));
        if (gameMode is >= 0 and <= 3)
            details.Add(new("游戏模式", gameMode switch { 0 => "生存", 1 => "创造", 2 => "冒险", _ => "旁观" }));
        if (difficulty is not null) details.Add(new("难度", difficulty));
        if (hardcore is not null) details.Add(new("极限模式", hardcore.Value ? "开启" : "关闭"));
        if (commands is not null) details.Add(new("允许命令", commands.Value ? "是" : "否"));
        if (lastPlayed is > 0 && lastPlayed <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
            details.Add(new("上次游玩", DateTimeOffset.FromUnixTimeMilliseconds(lastPlayed.Value).ToLocalTime().ToString("g")));
        return new(title, Details: details, IconPath: Icon);
    }

    private static (string? Name, int? Id) ReadVersion(NbtReader reader)
    {
        string? name = null;
        int? id = null;
        while (reader.ReadTag() is var type && type != Tag.End)
        {
            switch ((reader.ReadString(), type))
            {
                case ("Name", Tag.String): name = DisplayText(reader.ReadString()); break;
                case ("Id", Tag.Int): id = reader.ReadInt32(); break;
                default: reader.Skip(type); break;
            }
        }
        return (name, id);
    }

    private static string? ReadDifficulty(NbtReader reader)
    {
        string? value = null;
        while (reader.ReadTag() is var type && type != Tag.End)
        {
            if (reader.ReadString() == "difficulty" && type == Tag.String)
                value = reader.ReadString();
            else
                reader.Skip(type);
        }
        return value switch { "peaceful" => "和平", "easy" => "简单", "normal" => "普通", "hard" => "困难", _ => null };
    }

    private static string? DisplayText(string value)
    {
        var text = string.Concat(value.EnumerateRunes().Where(character => !Rune.IsControl(character))
            .Take(512).Select(character => character.ToString())).Trim();
        return text.Length == 0 ? null : text;
    }

    private enum Tag : byte { End, Byte, Short, Int, Long, Float, Double, ByteArray, String, List, Compound, IntArray, LongArray }

    private sealed class NbtReader(Stream stream)
    {
        private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        private int _remainingBytes = 8 * 1024 * 1024;
        private int _remainingTags = 100_000;

        internal Tag ReadTag() => (Tag)ReadByte();
        internal byte ReadByte()
        {
            Span<byte> bytes = stackalloc byte[1];
            Read(bytes);
            return bytes[0];
        }

        private ushort ReadStringLength()
        {
            Span<byte> bytes = stackalloc byte[2];
            Read(bytes);
            return BinaryPrimitives.ReadUInt16BigEndian(bytes);
        }

        internal int ReadInt32()
        {
            Span<byte> bytes = stackalloc byte[4];
            Read(bytes);
            return BinaryPrimitives.ReadInt32BigEndian(bytes);
        }

        internal long ReadInt64()
        {
            Span<byte> bytes = stackalloc byte[8];
            Read(bytes);
            return BinaryPrimitives.ReadInt64BigEndian(bytes);
        }

        internal string ReadString()
        {
            var bytes = new byte[ReadStringLength()];
            Read(bytes);
            // Some save editors write standard UTF-8. Both encodings match for most strings.
            try { return Utf8.GetString(bytes); }
            catch (DecoderFallbackException) { }
            // Java NBT uses modified UTF-8, encoding supplementary characters as two UTF-16 code units.
            var chars = new char[bytes.Length];
            var count = 0;
            for (var offset = 0; offset < bytes.Length;)
            {
                var value = (int)bytes[offset++];
                if ((value & 0xe0) == 0xc0)
                    value = ((value & 0x1f) << 6) | Continuation(bytes, ref offset);
                else if ((value & 0xf0) == 0xe0)
                    value = ((value & 0x0f) << 12) | (Continuation(bytes, ref offset) << 6) | Continuation(bytes, ref offset);
                else if (value >= 0x80)
                    throw new InvalidDataException("无效的 NBT 字符串。");
                chars[count++] = (char)value;
            }
            return new string(chars, 0, count);
        }

        private static int Continuation(byte[] bytes, ref int offset)
        {
            if (offset == bytes.Length || (bytes[offset] & 0xc0) != 0x80)
                throw new InvalidDataException("无效的 NBT 字符串。");
            return bytes[offset++] & 0x3f;
        }

        internal void SkipString() => Discard(ReadStringLength());

        internal void Skip(Tag type, int depth = 0)
        {
            if (depth >= 64 || --_remainingTags < 0) throw new InvalidDataException("NBT 标签数量或嵌套深度超限。");
            switch (type)
            {
                case Tag.Byte or Tag.Short or Tag.Int or Tag.Long or Tag.Float or Tag.Double:
                    Discard(FixedSize(type));
                    break;
                case Tag.ByteArray: Discard(ReadInt32()); break;
                case Tag.IntArray: Discard(4L * ReadInt32()); break;
                case Tag.LongArray: Discard(8L * ReadInt32()); break;
                case Tag.String: SkipString(); break;
                case Tag.List:
                    var element = ReadTag();
                    var count = ReadInt32();
                    if (count < 0 || count > _remainingBytes || element > Tag.LongArray || element == Tag.End && count != 0)
                        throw new InvalidDataException("无效的 NBT 列表。");
                    var width = FixedSize(element);
                    if (width != 0) Discard((long)width * count);
                    else for (var i = 0; i < count; i++) Skip(element, depth + 1);
                    break;
                case Tag.Compound:
                    while (ReadTag() is var child && child != Tag.End)
                    {
                        SkipString();
                        Skip(child, depth + 1);
                    }
                    break;
                default: throw new InvalidDataException("未知的 NBT 标签。");
            }
        }

        private static int FixedSize(Tag type) => type switch
        {
            Tag.Byte => 1, Tag.Short => 2, Tag.Int or Tag.Float => 4, Tag.Long or Tag.Double => 8, _ => 0
        };

        private void Discard(long length)
        {
            if (length < 0 || length > _remainingBytes) throw new InvalidDataException("NBT 数据超限。");
            Span<byte> buffer = stackalloc byte[1024];
            while (length > 0)
            {
                var count = (int)Math.Min(buffer.Length, length);
                Read(buffer[..count]);
                length -= count;
            }
        }

        private void Read(Span<byte> bytes)
        {
            if (bytes.Length > _remainingBytes) throw new InvalidDataException("NBT 数据超限。");
            stream.ReadExactly(bytes);
            _remainingBytes -= bytes.Length;
        }
    }
}
