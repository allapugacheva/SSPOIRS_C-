using System.Collections.Concurrent;
using System.Diagnostics;

public class DynamicThreadPool
{
    private volatile bool _running = true;

    private readonly BlockingCollection<Action> _taskQueue = new();
    private readonly List<Thread> _threads = new();
    private readonly Lock _lock = new();
    private readonly ConcurrentDictionary<int, bool> _threadsStatus;
        
    private readonly int _maxThreads;
    private readonly int _minThreads;

    private int _activeThreads = 0;
    private volatile bool _tasksRunning = true;
    
    public int CurrentAmountThread
    {
        get
        {
            lock (_lock)
            {
               return _threads.Count;
            }
        }
    } 
    
    public int AmountActiveThreads => _activeThreads;
    public int AmountRequests => _taskQueue.Count;

    public DynamicThreadPool(int minThreads, int maxThreads)
    {
        _maxThreads = maxThreads;
        _minThreads = minThreads;
        _threadsStatus = new ConcurrentDictionary<int, bool>();

        for (var i = 0; i < minThreads; i++)
        {
            StartNewThread();
        }

        new Thread(MonitorLoad) { IsBackground = true }.Start();
    }

    private void StartNewThread()
    {
        var thread = new Thread(() =>
            {
                int threadId = Thread.CurrentThread.ManagedThreadId;
                _threadsStatus[threadId] = false;

                try
                {
                    while (_running)
                    {
                        if (!_tasksRunning)
                        {
                            Thread.Sleep(10); 
                            continue;
                        }

                        if (_taskQueue.TryTake(out var task, Timeout.Infinite))
                        {
                            _threadsStatus[threadId] = true;
                            Interlocked.Increment(ref _activeThreads);
                            try
                            {
                                task.Invoke();
                            }
                            finally
                            {
                                Interlocked.Decrement(ref _activeThreads);
                                _threadsStatus[threadId] = false;
                            }
                        }
                    }
                }
                catch (ThreadInterruptedException) { }
            })
            { IsBackground = true };

        lock (_lock)
        {
            _threadsStatus.TryAdd(thread.ManagedThreadId, false);
            _threads.Add(thread);
            thread.Start();
        }
    }


    private void MonitorLoad()
    {
        var clearTimer = new Stopwatch();
        clearTimer.Start();

        while (_running)
        {
            Thread.Sleep(500);

            lock (_lock)
            {
                _tasksRunning = false;
                
                var queueSize = _taskQueue.Count;
                var currentThreads = _threads.Count;

                if (queueSize > 0 && _activeThreads == currentThreads && currentThreads < _maxThreads)
                {
                    var newThreads = Math.Min(3, _maxThreads - currentThreads);
                    for (var i = 0; i < newThreads; i++)
                    {
                        StartNewThread();
                    }

                    clearTimer.Restart();
                }
                else if (queueSize == 0 && currentThreads > _minThreads
                                        && clearTimer.Elapsed.TotalSeconds > 20.0
                                        && _activeThreads < _minThreads)
                {
                    var sleepingThreads = _threads
                        .Where(thread => _threadsStatus.TryGetValue(thread.ManagedThreadId, out bool isActive) && !isActive)
                        .ToList();

                    foreach (var thread in sleepingThreads)
                    {
                        if (_threads.Count == _minThreads)
                            break;

                        thread.Interrupt();
                        thread.Join();
                        _threads.Remove(thread);
                        _threadsStatus.TryRemove(thread.ManagedThreadId, out _);
                    }

                    clearTimer.Restart();
                }
                
                if(_activeThreads >= _minThreads)
                    clearTimer.Restart();
                
                _tasksRunning = true;
            }
        }
    }

    public void EnqueueTask(Action task)
    {
        _taskQueue.Add(task);
    }

    public void Stop()
    {
        _running = false;
        _taskQueue.CompleteAdding();

        lock (_lock)
        {
            foreach (var thread in _threads)
            {
                thread.Interrupt();
                thread.Join();
            }
            _threads.Clear();
        }
    }
}