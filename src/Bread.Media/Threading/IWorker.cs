using System.Threading.Tasks;

namespace Bread.Media;

/// <summary>
/// Enumerates the different states in which a worker can be.
/// </summary>
public enum WorkerState
{
    /// <summary>
    /// The worker has been created and it is ready to start.
    /// </summary>
    Created,

    /// <summary>
    /// The worker is running it cycle logic.
    /// </summary>
    Running,

    /// <summary>
    /// The worker is in the paused or suspended state.
    /// </summary>
    Paused,

    /// <summary>
    /// The worker is stopped and ready for disposal.
    /// </summary>
    Stopped
}

/// <summary>
/// Defines a standard API to control background application workers.
/// </summary>
/// <seealso cref="IDisposable" />
public interface IWorker : IDisposable
{
    /// <summary>
    /// Gets the name identifier of this worker.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the current state of the worker.
    /// </summary>
    WorkerState State { get; }

    /// <summary>
    /// Gets a value indicating whether this instance is disposed.
    /// </summary>
    /// <value>
    ///   <c>true</c> if this instance is disposed; otherwise, <c>false</c>.
    /// </value>
    bool IsDisposed { get; }

    /// <summary>
    /// Starts execution of worker cycles.
    /// </summary>
    /// <returns>The awaitable task.</returns>
    Task<WorkerState> StartAsync();

    /// <summary>
    /// Pauses execution of worker cycles.
    /// </summary>
    /// <returns>The awaitable task.</returns>
    Task<WorkerState> PauseAsync();

    /// <summary>
    /// Resumes execution of worker cycles.
    /// </summary>
    /// <returns>The awaitable task.</returns>
    Task<WorkerState> ResumeAsync();

    /// <summary>
    /// Permanently stops execution of worker cycles.
    /// An interrupt is always sent to the worker. If you wish to stop
    /// the worker without interrupting then call the <see cref="PauseAsync"/>
    /// method, await it, and finally call the <see cref="StopAsync"/> method.
    /// </summary>
    /// <returns>The awaitable task.</returns>
    Task<WorkerState> StopAsync();
}
