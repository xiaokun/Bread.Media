using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Bread.Media;

/// <summary>
/// A base class for implementing interval workers which executed at a shared thread.
/// Suitable for simple tasks that can be handled quickly. 
/// </summary>
public abstract class ShareThreadWorker : WorkerBase
{
    private readonly InnerSharedWorkdThead QuantumTimer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareThreadWorker"/> class.
    /// </summary>
    /// <param name="name">The name.</param>
    protected ShareThreadWorker(string name)
        : base(name)
    {
        QuantumTimer = new InnerSharedWorkdThead(OnQuantumTicked);
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();
        QuantumTimer.Dispose();
    }

    /// <summary>
    /// Called when every quantum of time occurs.
    /// </summary>
    private void OnQuantumTicked()
    {
        if (!TryBeginCycle())
            return;

        ExecuteCyle(20);
    }
}


/// <summary>
/// Defines a timer for discrete event firing.
/// Execution of callbacks is ensured non re-entrant.
/// A single thread is used to execute callbacks in <see cref="ThreadPool"/> threads
/// for all registered <see cref="InnerSharedWorkdThead"/> instances. This effectively reduces
/// the amount <see cref="Timer"/> instances when many of such objects are required.
/// </summary>
public sealed class InnerSharedWorkdThead : IDisposable
{
    private static readonly Stopwatch Stopwatch = new Stopwatch();
    private static readonly List<InnerSharedWorkdThead> RegisteredTimers = new List<InnerSharedWorkdThead>();
    private static readonly ConcurrentQueue<InnerSharedWorkdThead> PendingAddTimers = new ConcurrentQueue<InnerSharedWorkdThead>();
    private static readonly ConcurrentQueue<InnerSharedWorkdThead> PendingRemoveTimers = new ConcurrentQueue<InnerSharedWorkdThead>();

    private static readonly Thread TimerThread = new Thread(ExecuteCallbacks) {
        IsBackground = true,
        Name = nameof(InnerSharedWorkdThead),
        Priority = ThreadPriority.AboveNormal
    };

    private static double TickCount;

    private readonly Action UserCallback;
    private int m_IsDisposing;
    private int m_IsRunningCycle;

    /// <summary>
    /// Initializes static members of the <see cref="InnerSharedWorkdThead"/> class.
    /// </summary>
    static InnerSharedWorkdThead()
    {
        Resolution = Constants.DefaultTimingPeriod;
        Stopwatch.Start();
        TimerThread.Start();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InnerSharedWorkdThead"/> class.
    /// </summary>
    /// <param name="callback">The callback.</param>
    public InnerSharedWorkdThead(Action callback)
    {
        UserCallback = callback;
        PendingAddTimers.Enqueue(this);
    }

    /// <summary>
    /// Gets the current time interval at which callbacks are being enqueued.
    /// </summary>
    public static TimeSpan Resolution
    {
        get;
        private set;
    }

    /// <summary>
    /// Gets or sets a value indicating whether this instance is running cycle to prevent reentrancy.
    /// </summary>
    private bool IsRunningCycle
    {
        get => Interlocked.CompareExchange(ref m_IsRunningCycle, 0, 0) != 0;
        set => Interlocked.Exchange(ref m_IsRunningCycle, value ? 1 : 0);
    }

    /// <summary>
    /// Gets or sets a value indicating whether this instance is disposing.
    /// </summary>
    private bool IsDisposing
    {
        get => Interlocked.CompareExchange(ref m_IsDisposing, 0, 0) != 0;
        set => Interlocked.Exchange(ref m_IsDisposing, value ? 1 : 0);
    }

    /// <summary>
    /// Releases unmanaged and - optionally - managed resources.
    /// </summary>
    public void Dispose()
    {
        IsRunningCycle = true;
        if (IsDisposing) return;
        IsDisposing = true;
        PendingRemoveTimers.Enqueue(this);
    }

    /// <summary>
    /// Implements the execute-wait cycles of the thread.
    /// </summary>
    /// <param name="state">The state.</param>
    private static void ExecuteCallbacks(object? state)
    {
        while (true) {
            TickCount++;
            if (TickCount >= 60) {
                Resolution = TimeSpan.FromMilliseconds(Stopwatch.Elapsed.TotalMilliseconds / TickCount);
                Stopwatch.Restart();
                TickCount = 0;

                // Debug.WriteLine($"Timer Resolution is now {Resolution.TotalMilliseconds}");
            }

            System.Threading.Tasks.Parallel.ForEach(RegisteredTimers, (t) => {
                if (t.IsRunningCycle || t.IsDisposing)
                    return;

                t.IsRunningCycle = true;

                // async run internal work in thread pool 
                Task.Run(() => {
                    try {
                        t.UserCallback?.Invoke();
                    }
                    finally {
                        t.IsRunningCycle = false;
                    }
                });
            });

            while (PendingAddTimers.TryDequeue(out var addTimer))
                RegisteredTimers.Add(addTimer);

            while (PendingRemoveTimers.TryDequeue(out var remTimer))
                RegisteredTimers.Remove(remTimer);

            Task.Delay(Constants.DefaultTimingPeriod).Wait();
        }
    }
}
