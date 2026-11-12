// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

namespace Wonder.Extensibility.Contracts;

/// <summary>
/// Entry point for a trusted local plugin. Activation and disposal must return promptly.
/// Dispose releases all activity and references acquired during activation.
/// </summary>
public interface IWonderPlugin : IDisposable
{
    void Activate(IPluginContext context);
}

public interface IPluginContext
{
    /// <summary>A JSON object captured at activation. Saving a new configuration restarts the plugin.</summary>
    string ConfigurationJson { get; }

    /// <summary>A persistent directory reserved for the plugin's data.</summary>
    string DataDirectory { get; }

    /// <summary>Adds a message to the plugin manager's recent activity.</summary>
    void Log(string message);
}
