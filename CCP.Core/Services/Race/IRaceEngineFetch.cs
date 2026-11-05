using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Race
{
    // The engine-fetch lane supplies this service. Installed means verified, not launched.
    // Cancellation must stop the transfer and clean up partial files before the task completes.
    public enum RaceEngineFetchResult { Installed, Network, Checksum, Storage, Unsupported, Unavailable }
    public interface IRaceEngineFetch
    {
        bool IsInstalled { get; }
        Task<RaceEngineFetchResult> FetchAsync(IProgress<double> progress, CancellationToken cancellationToken);
    }
}
