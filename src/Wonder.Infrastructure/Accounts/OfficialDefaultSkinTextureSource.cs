// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Wonder.Infrastructure;

internal sealed class OfficialDefaultSkinTextureSource(HttpClient http)
{
    // The immutable 26.3 client from Mojang's version manifest. Only the contiguous
    // 18 default-skin entries are requested. No texture data ships with Wonder.
    internal const string Url = "https://piston-data.mojang.com/v1/objects/e877b6a07acd633fb3bb475002175cec036e7b87/client.jar";
    internal const long ClientSize = 41_483_720;
    internal const int Start = 12_592_089;
    internal const int End = 12_610_855;
    internal const int Length = End - Start + 1;

    public static OfficialDefaultSkinTextureSource Shared { get; } = new(new HttpClient(new HttpClientHandler
    {
        AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None
    }));
    private readonly SemaphoreSlim _load = new(1, 1);
    private volatile Dictionary<string, byte[]>? _cache;

    private static readonly Entry[] Entries =
    [
        new("slim/alex.png", 12592089, 963, 958, "99391ED9DFF9581EC88DAF23B9B028ADC9AC91F52889EDCFF1C982D0923C0CFF"),
        new("slim/ari.png", 12593150, 855, 850, "448BDA6817FA3894632CC11751F3F080DDE917B2AD1679ED106F107984289560"),
        new("slim/efe.png", 12594103, 964, 959, "FA3569F027CAADCC17EFBB1B46F8D0F2830C408CAFC774E1F3BF573F4BB7C419"),
        new("slim/kai.png", 12595165, 1098, 1093, "00B90F287BEBA1068032EE973F8B890394A6999F9F1B6CE5DB7E58CA47384760"),
        new("slim/makena.png", 12596364, 1077, 1072, "197307BF92FB9D3ADB5808593C88E48AB152335CC4276DB4127CCB82D010CA68"),
        new("slim/noor.png", 12597540, 828, 823, "09F9306ED924B1ADBB97837D4CA8D09D7057731A26CDECD2076BC8601F752589"),
        new("slim/steve.png", 12598468, 843, 838, "1FF866CB8418B18D0052089CFF27DFB90F8FC9727A8CEA83E0E876819B58CE19"),
        new("slim/sunny.png", 12599411, 952, 947, "32FF3BE1394115BBBBCB3B6AEA20F0CD496338D31A5341EF73CF1DF6BC963DBE"),
        new("slim/zuri.png", 12600462, 883, 878, "4F9333C958C31268FC813DB3B424FB1354D81085BA0BE6B112C809390847E350"),
        new("wide/alex.png", 12601537, 973, 968, "C8D78144F653A903D5B4118B94BD808C5522EDC300BCA3B674A2FFBB3A388DFD"),
        new("wide/ari.png", 12602608, 864, 859, "B77BFCA4FF607C1D72AD26867549FB7F0A9317C0811424A189D72CD0D47CA32A"),
        new("wide/efe.png", 12603570, 974, 969, "6E2E2D07136D057997B62ED5A256A577A7678D6F0EF05A7A402CB5096A11143D"),
        new("wide/kai.png", 12604642, 1106, 1101, "F5B3AF3440257759F074AE07B630A65195D7222241DC7AF1189B954CF7FC3F76"),
        new("wide/makena.png", 12605849, 1068, 1063, "E58A664738D0073C348265052AB60A0EBF15F271A1B6AAEEC0E47C027BC7CD0E"),
        new("wide/noor.png", 12607016, 837, 832, "66343B7A4D6A63AF1ABED71437728794A9C5D5FC666A10A3CCE666E5759AC3FF"),
        new("wide/steve.png", 12607953, 853, 848, "06628F9CEF88520DA742AB280A01677CE17C99717F964ED89ED791673121094C"),
        new("wide/sunny.png", 12608906, 956, 951, "C1CF854C5C244A9F07E84B2FABA067C20D3CB1156899CA43F85F0EFC9A954270"),
        new("wide/zuri.png", 12609961, 895, 890, "ADC7F1B62CDF791096493D14C3EDC8B36FD940E4BF10093FC48F461B0F0A3DBA")
    ];

    public byte[]? TryRead(string name) => _cache?.GetValueOrDefault(name)?.ToArray();

    public async Task<byte[]?> GetAsync(string name, CancellationToken ct)
    {
        if (TryRead(name) is { } cached) return cached;
        await _load.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (TryRead(name) is { } resolved) return resolved;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var request = new HttpRequestMessage(HttpMethod.Get, Url);
            request.Headers.Range = new RangeHeaderValue(Start, End);
            request.Headers.AcceptEncoding.ParseAdd("identity");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            var headers = response.Content.Headers;
            var range = headers.ContentRange;
            if (response.StatusCode != HttpStatusCode.PartialContent || range is null || range.Unit != "bytes" ||
                range.From != Start || range.To != End || range.Length != ClientSize ||
                headers.ContentLength is long contentLength && contentLength != Length || headers.ContentEncoding.Any(encoding => encoding != "identity")) return null;
            var raw = await AccountHttp.ReadBoundedAsync(response.Content, Length, timeout.Token).ConfigureAwait(false);
            if (raw.Length != Length) return null;
            var textures = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var entry in Entries)
            {
                ct.ThrowIfCancellationRequested();
                using var slice = new MemoryStream(raw, entry.Offset - Start, entry.CompressedLength, writable: false);
                using var png = new DeflateStream(slice, CompressionMode.Decompress);
                var bytes = new byte[entry.PngLength];
                png.ReadExactly(bytes);
                if (png.ReadByte() != -1 || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(entry.Sha256, StringComparison.Ordinal)) return null;
                MinecraftSkinImage.Validate(bytes);
                textures.Add(entry.Name, bytes);
            }
            _cache = textures;
            return TryRead(name);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or Wonder.Application.AccountAuthenticationException) { return null; }
        finally { _load.Release(); }
    }

    private sealed record Entry(string Name, int Offset, int CompressedLength, int PngLength, string Sha256);
}
