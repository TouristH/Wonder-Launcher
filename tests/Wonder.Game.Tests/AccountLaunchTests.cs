// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Security.Cryptography;
using Wonder.Application;
using Wonder.Core;
using Wonder.Infrastructure;

namespace Wonder.Game.Tests;

public sealed class AccountLaunchTests
{
    private const string Token = "synthetic-game-access-token-not-a-real-credential";
    private const string Uuid = "123456781234123412341234567890ab";
    private static GameSession MicrosoftSession() => new(AccountKind.Microsoft, "OnlinePlayer", Uuid, Token,
        "fab70daf-af0d-423d-9468-6b42edce3c81", "123456789");

    [Fact]
    public async Task Microsoft_identity_replaces_every_case_variant_from_metadata()
    {
        using var f = new ImporterFixtures(); var instance = await Instance(f);
        f.Json(".minecraft/versions/1.20.1/1.20.1.json", new { id = "1.20.1", mainClass = "Main", arguments = new { game = new[] {
            "--USERNAME=Other", "--uuid", "bad", "--ACCESSTOKEN", "stale", "--session", "stale", "--clientId", "stale", "--xuid", "stale" } } });
        var plan = await Launcher().BuildPlanAsync(new(instance, "OfflinePlayer", f.DirectoryAt("cache")) { Session = MicrosoftSession() });
        Assert.Equal("OnlinePlayer", Option(plan, "--username")); Assert.Equal(Uuid, Option(plan, "--uuid"));
        Assert.Equal(Token, Option(plan, "--accessToken")); Assert.Equal("msa", Option(plan, "--userType"));
        Assert.Equal("123456789", Option(plan, "--xuid")); Assert.Contains(Token, Option(plan, "--session"));
        Assert.DoesNotContain("stale", plan.Arguments); Assert.DoesNotContain("--USERNAME=Other", plan.Arguments);
        Assert.DoesNotContain(Token, plan.RedactOutput("--accessToken " + Token));
        Assert.DoesNotContain(Token, plan.ToString());
    }

    [Theory]
    [InlineData("-javaagent:untrusted.jar")]
    [InlineData("-Dauthlibinjector.yggdrasil=https://third.example/")]
    [InlineData("-Djavax.net.ssl.trustStore=untrusted")]
    [InlineData("-agentpath:untrusted.dll")]
    public async Task Microsoft_launch_rejects_authentication_interceptors(string argument)
    {
        using var f = new ImporterFixtures(); var instance = (await Instance(f)) with { Overrides = new() { JvmArguments = argument } };
        await Assert.ThrowsAsync<InvalidDataException>(() => Launcher().BuildPlanAsync(new(instance, "Player", f.DirectoryAt("cache")) { Session = MicrosoftSession() }));
        f.Json(".minecraft/versions/1.20.1/1.20.1.json", new { id = "1.20.1", mainClass = "Main", arguments = new { jvm = new[] { argument } } });
        await Assert.ThrowsAsync<InvalidDataException>(() => Launcher().BuildPlanAsync(new(instance with { Overrides = new() }, "Player", f.DirectoryAt("cache")) { Session = MicrosoftSession() }));
    }

    [Fact]
    public async Task Microsoft_logging_metadata_cannot_append_an_agent_after_validation()
    {
        using var f = new ImporterFixtures(); var instance = await Instance(f);
        f.Json(".minecraft/versions/1.20.1/1.20.1.json", new
        {
            id = "1.20.1", mainClass = "Main",
            logging = new { client = new { type = "log4j2-xml", argument = "-javaagent:untrusted.jar" } }
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => Launcher().BuildPlanAsync(new(instance, "Player", f.DirectoryAt("cache")) { Session = MicrosoftSession() }));
    }

    [Fact]
    public async Task Third_party_unicode_role_supported_by_provider_is_preserved_for_game()
    {
        using var f = new ImporterFixtures(); var instance = await Instance(f);
        var jar = f.Write("trusted-agent.jar", "synthetic jar bytes");
        var session = new GameSession(AccountKind.Yggdrasil, "奇想玩家", Uuid, Token, serverUrl: "https://skin.example/",
            authlibInjectorPath: jar, authlibInjectorSha256: Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(jar))));
        var plan = await Launcher().BuildPlanAsync(new(instance, "Player", f.DirectoryAt("cache")) { Session = session });
        Assert.Equal("奇想玩家", Option(plan, "--username"));
    }

    [Fact]
    public async Task Third_party_agent_is_pinned_and_tampering_fails_before_process_start()
    {
        using var f = new ImporterFixtures(); var instance = await Instance(f);
        var jar = f.Write("trusted-agent.jar", "synthetic jar bytes");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(jar)));
        var session = new GameSession(AccountKind.Yggdrasil, "SkinPlayer", Uuid, Token,
            serverUrl: "https://skin.example/api/yggdrasil/", authlibInjectorPath: jar, authlibInjectorSha256: hash);
        var plan = await Launcher().BuildPlanAsync(new(instance, "Player", f.DirectoryAt("cache")) { Session = session });
        Assert.Contains("-javaagent:" + jar + "=https://skin.example/api/yggdrasil/", plan.Arguments);
        Assert.Equal("mojang", Option(plan, "--userType"));
        File.AppendAllText(jar, "tampered");
        await Assert.ThrowsAsync<InvalidDataException>(() => Launcher().RunAsync(plan, () => Assert.Fail("Must not start"), _ => { }));
        await Assert.ThrowsAsync<InvalidDataException>(() => Launcher().BuildPlanAsync(new(instance, "Player", f.DirectoryAt("cache")) { Session = session }));
    }

    [Theory]
    [InlineData("--ACCESSTOKEN=evil")]
    [InlineData("--UserType legacy")]
    [InlineData("--SESSION evil")]
    public async Task Custom_game_parameters_cannot_replace_authenticated_identity(string argument)
    {
        using var f = new ImporterFixtures(); var instance = (await Instance(f)) with { Overrides = new() { GameArguments = argument } };
        await Assert.ThrowsAsync<InvalidDataException>(() => Launcher().BuildPlanAsync(new(instance, "Player", f.DirectoryAt("cache")) { Session = MicrosoftSession() }));
    }

    [Fact]
    public async Task Authentication_failure_has_no_offline_fallback_and_cancel_does_not_start_game()
    {
        var launcher = new RecordingLauncher();
        var service = Service(launcher, new Provider(_ => Task.FromException<GameSession>(new AccountAuthenticationException("请重新登录"))));
        await service.LaunchSelectedAsync();
        Assert.Equal(0, launcher.Builds); Assert.Equal(LaunchPhase.Failed, service.LaunchPhase);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service = Service(launcher, new Provider(async ct => { entered.SetResult(); await Task.Delay(Timeout.Infinite, ct); return MicrosoftSession(); }));
        var launch = service.LaunchSelectedAsync(); await entered.Task;
        service.CancelPreparation(); await launch;
        Assert.Equal(0, launcher.Builds); Assert.Equal(LaunchPhase.Idle, service.LaunchPhase);
    }

    [Fact]
    public async Task Service_redacts_game_output_and_failure_even_when_launcher_does_not()
    {
        var launcher = new RecordingLauncher();
        var service = Service(launcher, new Provider(_ => Task.FromResult(MicrosoftSession())));
        await service.LaunchSelectedAsync();
        Assert.Same(launcher.Session, launcher.Request!.Session);
        Assert.All(service.LogLines, line => Assert.DoesNotContain(Token, line));
        Assert.DoesNotContain(Token, service.Error);
    }

    private static string Option(LaunchPlan plan, string option) => plan.Arguments[plan.Arguments.ToList().IndexOf(option) + 1];
    private static LocalGameLauncher Launcher() => new((path, _) => Task.FromResult(new JavaRuntime(path, 21, "amd64")));
    private static async Task<GameInstance> Instance(ImporterFixtures f)
    {
        var dir = f.Version(); f.Write(".minecraft/versions/1.20.1/1.20.1.jar", "fixture");
        return Assert.Single((await new InstanceDiscovery().DiscoverAsync(dir)).Instances);
    }
    private static InstanceService Service(RecordingLauncher launcher, ILaunchAccountProvider provider) => new(
        new Store(), new Discovery(), launcher, Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Wonder.AccountLaunch")), accounts: provider);
    private sealed class Store : IInstanceStore
    {
        public InstanceCatalog Load() { var instance = InstanceSettingsTests.ValidInstance(); return new() { Instances = [instance], SelectedInstanceId = instance.Id }; }
        public void Save(InstanceCatalog catalog) { }
    }
    private sealed class Discovery : IInstanceDiscovery
    { public Task<DiscoveryResult> DiscoverAsync(string path, CancellationToken ct = default) => Task.FromResult(new DiscoveryResult([], [])); }
    private sealed class Provider(Func<CancellationToken, Task<GameSession>> get) : ILaunchAccountProvider
    { public Task<GameSession> GetSelectedSessionAsync(CancellationToken ct = default) => get(ct); }
    private sealed class RecordingLauncher : IGameLauncher
    {
        public int Builds; public LaunchRequest? Request; public GameSession? Session;
        public Task<LaunchPlan> BuildPlanAsync(LaunchRequest request, CancellationToken ct = default)
        { Builds++; Request = request; Session = request.Session; return Task.FromResult(new LaunchPlan("java", request.Instance.DefaultGameDirectory, ["Main"], request.Instance.Id)); }
        public Task<int> RunAsync(LaunchPlan plan, Action started, Action<string> output, CancellationToken ct = default)
        { started(); output("token = " + Token); throw new InvalidDataException("upstream echoed " + Token); }
    }
}
