using System.Threading;
using System.Threading.Tasks;
using Bread.Utility.Threading;

namespace Bread.Media;

public abstract class MediaWorker : WorkerBase, ISender
{
    private readonly AtomicBoolean _isTickChanged = new AtomicBoolean(false);

    private List<IListener> _listeners = new List<IListener>();
    private object _listenersLocker = new object(); // listeners locker

    private readonly Thread TimerThread = new Thread(ExecuteCallbacks) {
        IsBackground = true,
        Name = nameof(InnerSharedWorkdThead),
        Priority = ThreadPriority.AboveNormal
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaWorker"/> class.
    /// </summary>
    /// <param name="name">The name.</param>
    protected MediaWorker(string name)
        : base(name)
    {
        TimerThread.Start(this);
    }

    public void ConnectTo(IListener listener)
    {
        lock (_listenersLocker) {
            _listeners.Add(listener);
        }
    }

    public void Disconnect(IListener listener)
    {
        lock (_listenersLocker) {
            _listeners.Remove(listener);
        }
    }

    public bool Post(object e)
    {
        bool accepted = false;

        var list = new List<IListener>();
        lock (_listenersLocker) {
            foreach (var lis in _listeners) {
                list.Add(lis);
            }
        }

        foreach (var item in list) {
            if (item.QueueEvent(e)) {
                accepted = true;
            }
        }

        return accepted;
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();
        lock (_listenersLocker) {
            _listeners.Clear();
        }
    }

    /// <summary>
    /// Implements the execute-wait cycles of the thread.
    /// </summary>
    /// <param name="state">The state.</param>
    private static void ExecuteCallbacks(object? state)
    {
        var worker = state as MediaWorker;
        if (worker == null) return;

        int waittime = 20;

        while (true) {
            if (worker.State == WorkerState.Stopped) {
                break;
            }

            if (worker.TryBeginCycle() == false) {
                if (worker.State == WorkerState.Stopped) {
                    break;
                }
                waittime = 20;
            }
            else {
                waittime = worker.ExecuteCyle(20);
            }

            if (waittime > 0) {
                Task.Delay(waittime).Wait();
            }
        }
    }
}
