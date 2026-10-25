// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Core;

namespace Wonder.Application;

public interface IInstanceDiscovery
{
    Task<DiscoveryResult> DiscoverAsync(string selectedPath, CancellationToken ct = default);
}

public interface IInstanceStore
{
    InstanceCatalog Load();
    void Save(InstanceCatalog catalog);
}

public interface IGameLauncher
{
    Task<LaunchPlan> BuildPlanAsync(LaunchRequest request, CancellationToken ct = default);
    Task<int> RunAsync(LaunchPlan plan, Action started, Action<string> output, CancellationToken ct = default);
}

public enum LaunchPhase { Idle, Preparing, Running, Exited, Failed }
