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

    /// <summary>
    /// Returns a thread-safe snapshot of launcher interaction, or null when unavailable.
    /// Coordinates describe the named launcher window in device-independent pixels.
    /// No keyboard contents, credentials, game input or activity in other applications is included.
    /// </summary>
    PluginObservationSnapshot? GetObservation() => null;

    /// <summary>
    /// Registers a declarative desktop component during the synchronous Activate call (API 2).
    /// Its definition ID must begin with the plugin ID and a dot. The host removes registrations
    /// when activation fails or the plugin stops; application state and desktop positions remain.
    /// </summary>
    void RegisterComponent(PluginComponentRegistration registration) =>
        throw new NotSupportedException("This host does not support plugin components.");
}
