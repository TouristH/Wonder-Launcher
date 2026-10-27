// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text;
using Wonder.Application;
using Wonder.Core;
using Wonder.Infrastructure;

namespace Wonder.Game.Tests;

public sealed class WonderHdTextureTests
{
    [Theory]
    [InlineData(64, 64, false)]
    [InlineData(64, 32, false)]
    [InlineData(128, 128, false)]
    [InlineData(128, 64, false)]
    [InlineData(192, 192, false)]
    [InlineData(512, 256, false)]
    [InlineData(4096, 4096, false)]
    [InlineData(64, 32, true)]
    [InlineData(128, 64, true)]
    [InlineData(192, 96, true)]
    [InlineData(4096, 2048, true)]
    public void Third_party_preview_accepts_scaled_skin_and_cape_atlases(int width, int height, bool cape) =>
        MinecraftSkinImage.ValidatePreview(Png(width, height), cape, allowHighDefinition: true);

    [Theory]
    [InlineData(0, 64, false)]
    [InlineData(-64, 32, false)]
    [InlineData(32, 32, false)]
    [InlineData(96, 96, false)]
    [InlineData(128, 32, false)]
    [InlineData(128, 256, false)]
    [InlineData(4160, 4160, false)]
    [InlineData(int.MaxValue, int.MaxValue, false)]
    [InlineData(128, 128, true)]
    [InlineData(128, 0, true)]
    [InlineData(128, -64, true)]
    public void Third_party_preview_rejects_wrong_layouts_and_decoded_pixel_limits(int width, int height, bool cape)
    {
        var png = Png(64, 32);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), height);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(29), Crc(png.AsSpan(12, 17)));
        Assert.Throws<AccountAuthenticationException>(() => MinecraftSkinImage.ValidatePreview(png, cape, allowHighDefinition: true));
    }

    [Fact]
    public void Hd_preview_keeps_png_integrity_and_encoded_size_checks()
    {
        var valid = Png(128, 128);
        var corrupted = (byte[])valid.Clone(); corrupted[50] ^= 42;
        foreach (var png in new[] { corrupted, valid[..^1], valid.Concat(new byte[] { 0 }).ToArray(), new byte[16 * 1024 * 1024 + 1] })
            Assert.Throws<AccountAuthenticationException>(() => MinecraftSkinImage.ValidatePreview(png, cape: false, allowHighDefinition: true));
    }

    [Fact]
    public void Official_validation_preview_and_upload_remain_standard_resolution()
    {
        var png = Png(128, 128);
        Assert.Throws<AccountAuthenticationException>(() => MinecraftSkinImage.Validate(png));
        Assert.Throws<AccountAuthenticationException>(() => MinecraftSkinImage.ValidatePreview(png, cape: false, allowHighDefinition: false));
        Assert.Throws<AccountAuthenticationException>(() => MinecraftSkinImage.ValidatePreview(Png(128, 64), cape: true, allowHighDefinition: false));
        Assert.Throws<AccountAuthenticationException>(() => MinecraftSkinImage.ValidatePreview(Png(64, 64), cape: true, allowHighDefinition: false));
        using var handler = new Handler(() => throw new InvalidOperationException("Upload should fail before HTTP."));
        using var http = new HttpClient(handler);
        Assert.Throws<AccountAuthenticationException>(() =>
        {
            _ = new MicrosoftAuthenticator(http).UploadSkinAsync(new("test", AccountKind.Microsoft, "SyntheticPlayer", ""), new(), png, "classic");
        });
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task Hd_png_larger_than_one_mib_downloads_only_through_third_party_path()
    {
        var png = Png(640, 640, randomPixels: true);
        Assert.True(png.Length > 1024 * 1024);
        MinecraftSkinImage.ValidatePreview(png, cape: false, allowHighDefinition: true);
        using var handler = new Handler(() => new ByteArrayContent(png)); using var http = new HttpClient(handler);
        var client = new AccountTextureClient(http);
        Assert.Equal(png, await client.DownloadThirdPartyAsync("https://skin.example.test/hd.png"));
        Assert.False(handler.HadCredentials);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => client.DownloadAsync("https://textures.minecraft.net/texture/synthetic"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Third_party_download_bounds_known_and_streamed_response_lengths(bool knownLength)
    {
        var bytes = new byte[16 * 1024 * 1024 + 1];
        using var handler = new Handler(() => knownLength ? new ByteArrayContent(bytes) : new StreamContent(new UnseekableStream(bytes)));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => new AccountTextureClient(http).DownloadThirdPartyAsync("https://skin.example.test/large.png"));
        Assert.False(handler.HadCredentials);
    }

    private sealed class Handler(Func<HttpContent> content) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public bool HadCredentials { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            HadCredentials |= request.Headers.Authorization is not null || request.Headers.Contains("Cookie") || request.Content is not null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content() });
        }
    }

    private sealed class UnseekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private static byte[] Png(int width, int height, bool randomPixels = false)
    {
        using var png = new MemoryStream(); png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height); header[8] = 8; header[9] = 6;
        Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        {
            var row = new byte[width * 4 + 1]; var random = new Random(19);
            for (var y = 0; y < height; y++)
            {
                if (randomPixels) random.NextBytes(row.AsSpan(1));
                zlib.Write(row);
            }
        }
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", []); return png.ToArray();

        void Chunk(string type, byte[] data)
        {
            var name = Encoding.ASCII.GetBytes(type); Span<byte> value = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(value, data.Length); png.Write(value); png.Write(name); png.Write(data);
            var checksumBytes = new byte[name.Length + data.Length]; name.CopyTo(checksumBytes, 0); data.CopyTo(checksumBytes, name.Length);
            BinaryPrimitives.WriteUInt32BigEndian(value, Crc(checksumBytes)); png.Write(value);
        }
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
        }
        return ~crc;
    }
}
