using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Bread.Media;


public abstract class WorkerBase : IWorker
{
    private readonly object _stateLocker = new object();
    private readonly ManualResetEventSlim WaitForStateEvent = new ManualResetEventSlim(true);

    private int _state = (int)WorkerState.Created;
    private int _innerState = (int)WorkerState.Created;
    private CancellationTokenSource _cts = new CancellationTokenSource();

    protected WorkerBase(string name)
    {
        Name = name;
    }

    #region Properties

    /// <summary>
    /// Gets the name of the worker.
    /// </summary>
    public string Name { get; }


    #endregion

    #region Start, Stop, Pause, Resume

    /// <inheritdoc />
    public WorkerState State
    {
        get => (WorkerState)Interlocked.CompareExchange(ref _state, 0, 0);
        private set => Interlocked.Exchange(ref _state, (int)value);
    }

    /// <summary>
    /// Gets or sets the desired state of the worker.
    /// </summary>
    protected WorkerState DesiredState
    {
        get => (WorkerState)Interlocked.CompareExchange(ref _innerState, 0, 0);
        set => Interlocked.Exchange(ref _innerState, (int)value);
    }

    /// <inheritdoc />
    public Task<WorkerState> StartAsync()
    {
        lock (_stateLocker) {
            if (IsDisposed || IsDisposing)
                return Task.FromResult(State);

            if (State == WorkerState.Running || State == WorkerState.Stopped) {
                return Task.FromResult(State);
            }

            //Created to begin start, or Paused to resume.
            WaitForStateEvent.Reset();
            DesiredState = WorkerState.Running;
        }

        return WaitForWantedState();
    }

    /// <inheritdoc />
    public Task<WorkerState> PauseAsync()
    {
        lock (_stateLocker) {
            if (IsDisposed || IsDisposing)
                return Task.FromResult(State);

            if (State != WorkerState.Running)
                return Task.FromResult(State);

            // only Running to pause.
            WaitForStateEvent.Reset();
            DesiredState = WorkerState.Paused;
        }

        return WaitForWantedState();
    }

    /// <inheritdoc />
    public Task<WorkerState> ResumeAsync()
    {
        lock (_stateLocker) {
            if (IsDisposed || IsDisposing)
                return Task.FromResult(State);

            if (State == WorkerState.Running || State == WorkerState.Stopped)
                return Task.FromResult(State);

            //Created to begin start, or Paused to resume.
            WaitForStateEvent.Reset();
            DesiredState = WorkerState.Running;
        }

        return WaitForWantedState();
    }

    /// <inheritdoc />
    public Task<WorkerState> StopAsync()
    {
        lock (_stateLocker) {
            if (IsDisposed || IsDisposing)
                return Task.FromResult(State);

            if (State == WorkerState.Created)
                State = WorkerState.Stopped;

            if (State == WorkerState.Stopped)
                return Task.FromResult(WorkerState.Stopped);

            // Running or Paused to became stop.
            WaitForStateEvent.Reset();
            DesiredState = WorkerState.Stopped;
            Interrupt();
        }

        return WaitForWantedState();
    }

    /// <summary>
    /// Interrupts a cycle or a wait operation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void Interrupt() => _cts.Cancel();

    /// <summary>
    /// Returns a hot task that waits for the state of the worker to change.
    /// </summary>
    /// <returns>The awaitable state change task.</returns>
    private Task<WorkerState> WaitForWantedState() => Task.Run(() => {
        while (WaitForStateEvent.Wait(Constants.DefaultTimingPeriod) == false)
            Interrupt();

        return State;
    });

    #endregion


    #region Execute logic
    /// <summary>
    /// Invoked before first time become Running.
    /// </summary>
    /// <returns>false if failed.</returns>
    protected virtual bool Start() { return true; }

    protected virtual void Stop() { }

    /// <summary>
    /// Represents the user defined logic to be executed on a single worker cycle.
    /// Check the cancellation token continuously if you need responsive interrupts.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The time interval that need to be waitted</returns>
    protected abstract int ExecuteCycleLogic(CancellationToken ct);

    /// <summary>
    /// Handles the cycle logic exceptions.
    /// </summary>
    /// <param name="ex">The exception that was thrown.</param>
    protected virtual void OnCycleException(Exception ex)
    {
        // placeholder
    }

    protected void MarkAsStop()
    {
        lock (_stateLocker) {
            DesiredState = WorkerState.Stopped;
        }
    }

    /// <summary>
    /// Tries to acquire a cycle for execution.
    /// </summary>
    /// <returns>True if a cycle should be executed.</returns>
    protected bool TryBeginCycle()
    {
        var state = State;

        if (state == WorkerState.Stopped)
            return false;

        if (state == WorkerState.Created && DesiredState == WorkerState.Running) {
            if (Start() == false) {
                MarkAsStop();
            }
        }

        if (DesiredState == WorkerState.Stopped) {
            Stop();
        }

        lock (_stateLocker) {
            State = DesiredState;
            WaitForStateEvent.Set();

            if (State == WorkerState.Stopped)
                return false;
        }

        return true;
    }

    /// <summary>
    ///  Executes the cyle calling the user-defined code.
    /// </summary>
    protected int ExecuteCyle(int defaultTime)
    {
        // Recreate the token source -- applies to cycle logic and delay
        var ts = _cts;
        if (ts.IsCancellationRequested) {
            _cts = new CancellationTokenSource();
            ts.Dispose();
        }

        if (State == WorkerState.Running) {
            try {
                defaultTime = ExecuteCycleLogic(_cts.Token);
            }
            catch (Exception ex) {
                OnCycleException(ex);
                Log.Warn($"{Name}\t{ex.Message}");
                Log.Exception(ex);
                lock (_stateLocker) { DesiredState = WorkerState.Stopped; }
                return 0;
            }
        }
        return defaultTime;
    }

    #endregion

    #region IDisposable

    private int _isDisposed;
    private int _isDisposing;

    /// <inheritdoc />
    public bool IsDisposed
    {
        get => Interlocked.CompareExchange(ref _isDisposed, 0, 0) != 0;
        private set => Interlocked.Exchange(ref _isDisposed, value ? 1 : 0);
    }

    /// <summary>
    /// Gets a value indicating whether this instance is currently being disposed.
    /// </summary>
    /// <value>
    ///   <c>true</c> if this instance is disposing; otherwise, <c>false</c>.
    /// </value>
    protected bool IsDisposing
    {
        get => Interlocked.CompareExchange(ref _isDisposing, 0, 0) != 0;
        private set => Interlocked.Exchange(ref _isDisposing, value ? 1 : 0);
    }

    /// <summary>
    /// This method is called automatically when <see cref="Dispose()"/> is called.
    /// Makes sure you release all resources within this call.
    /// </summary>
    protected virtual void OnDisposing()
    {
        // placeholder
    }

    /// <inheritdoc />
    public virtual void Dispose() => Dispose(true);

    /// <summary>
    /// Releases unmanaged and optionally managed resources.
    /// </summary>
    /// <param name="alsoManaged">Determines if managed resources hsould also be released.</param>
    private void Dispose(bool alsoManaged)
    {
        if (IsDisposed) return;

        StopAsync().Wait();

        lock (_stateLocker) {
            if (IsDisposed || IsDisposing)
                return;

            IsDisposing = true;
            WaitForStateEvent.Set();
            try { OnDisposing(); } catch { /* Ignore */ }
            WaitForStateEvent.Dispose();
            _cts.Dispose();
            IsDisposed = true;
            IsDisposing = false;
        }
    }
    #endregion
}
