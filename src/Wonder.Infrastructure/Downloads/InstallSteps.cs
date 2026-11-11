// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Wonder.Core;

namespace Wonder.Infrastructure;

internal sealed class InstallSteps(IProgress<DownloadProgress>? sink, int total)
{
    private int _step;

    public IProgress<DownloadProgress>? Begin(string name)
    {
        if (sink is null) return null;
        var step = new StepProgress(sink, ++_step, total, name);
        step.Report(new(name));
        return step;
    }

    private sealed class StepProgress(IProgress<DownloadProgress> sink, int number, int total, string name) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => sink.Report(value with { StepNumber = number, TotalSteps = total, StepName = name });
    }
}
