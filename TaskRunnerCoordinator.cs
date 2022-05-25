using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace Throttler
{
    public class TaskRunnerCoordinator<TActionParam>
    {
        protected ConcurrentQueue<TaskInfo<TActionParam>> _qtasks = new();
        protected ConcurrentDictionary<int, TaskInfo<TActionParam>> _completedTasks = new();
        protected int _maxParallel;
        protected List<TaskRunner<TActionParam>> _runners = new();
        protected const int DEFAULT_FREE_RUNNER_WAIT_MS = 100;
        protected int _freeRunnerWaitMs = DEFAULT_FREE_RUNNER_WAIT_MS;
        protected int _lastTaskId;
        protected bool _waitingTasks;
        protected bool _processingTasks;
        protected DateTime? _lastNoFreeRunnerLog;
        protected int _minDelayMs;

        public TaskRunnerCoordinator(int maxParallel, int minDelayMilliseconds)
        { 
            if (maxParallel < 1 || maxParallel > 10)
                throw new ArgumentOutOfRangeException(nameof(maxParallel));
            if (minDelayMilliseconds < 100 || minDelayMilliseconds > 60 * 1000)
                throw new ArgumentOutOfRangeException(nameof(minDelayMilliseconds));
            _minDelayMs = minDelayMilliseconds;
            _maxParallel = maxParallel;
            for (var i = 0; i < _maxParallel; i++) {
                _runners.Add(new TaskRunner<TActionParam>(i, minDelayMilliseconds));
            }
        }

        protected TaskRunnerCoordinator(int maxParallel, int minDelayMilliseconds, bool internalCall)
        {
            if (maxParallel < 1 || maxParallel > 5)
                throw new ArgumentOutOfRangeException(nameof(maxParallel));
            if (minDelayMilliseconds < 100 || minDelayMilliseconds > 60 * 1000)
                throw new ArgumentOutOfRangeException(nameof(minDelayMilliseconds));
            _minDelayMs = minDelayMilliseconds;
            _maxParallel = maxParallel;
        }

        public virtual TaskInfo<TActionParam> GetCompletedTaskInfo(int id)
        {
            if (!_completedTasks.ContainsKey(id))
                return null;
            return _completedTasks[id];
        }

        public virtual int Enqueue(Action<TActionParam> t, TActionParam arg)
        {
            if (_waitingTasks)
                throw new InvalidOperationException("Cannot enqueue new tasks if a waiting operation is in place.");
            var n = Interlocked.Increment(ref _lastTaskId);
            var ti = new TaskInfo<TActionParam>() { Id = n, Method = t, Argument = arg };
            Logger.Log($"Equeuing task #{n}. Args={arg}");
            _qtasks.Enqueue(ti);
            return n;
        }

        public async Task WaitForTasksToComplete()
        {
            if (_waitingTasks)
                throw new InvalidOperationException($"Called {nameof(WaitForTasksToComplete)} more than once.");

            _waitingTasks = true;
            while (true) {
                var allFree = true;
                foreach (var r in _runners)
                {
                    if (r.LockState != TaskRunner<TActionParam>.LockStateEnum.Free)
                    {
                        allFree = false;
                        break;
                    }
                }
                if (allFree && !_processingTasks)
                {
                    _waitingTasks = false;
                    return;
                }
                await Task.Delay(50);
                await RelaseCompletedRunners();
            }
        }

        public virtual async Task ProcessTasks()
        {
            try
            {
                _processingTasks = true;
                while (_qtasks.Count > 0 && !_waitingTasks)
                {
                    if (_qtasks.TryDequeue(out TaskInfo<TActionParam> ti))
                    {
                        var runner = await FindFreeRunner();
                        while (runner is null && !_waitingTasks)
                        {
                            if (!_lastNoFreeRunnerLog.HasValue || (DateTime.Now - _lastNoFreeRunnerLog.Value).TotalMilliseconds > _minDelayMs)
                            {
                                _lastNoFreeRunnerLog = DateTime.Now;
                                Logger.Log("No free runner found. Checking completed runners...");
                            }
                            // no free runner found
                            // check if any busy runner has completed
                            await RelaseCompletedRunners();
                            await Task.Delay(_freeRunnerWaitMs);
                            runner = await FindFreeRunner();
                        }
                        if (_waitingTasks)
                            return;
                        Logger.Log($"Runner {runner.Id} is assigned task #{ti.Id}. Args={ti.Argument}");
                        await runner.RunTask(ti);
                    }
                    else
                        await Task.Delay(_freeRunnerWaitMs);
                }
            }
            finally
            {
                _processingTasks = false;
            }
        }

        protected async Task<bool> RelaseCompletedRunners()
        {
            foreach (var r in _runners)
            {
                if (r.LockState == TaskRunner<TActionParam>.LockStateEnum.Completed)
                {
                    // move the taskinfo to the completed list
                    _completedTasks.AddOrUpdate(r.AssignedTask.Id, r.AssignedTask, (k, t) => t);
                    await r.Unlock();
                    return true;
                }
            }
            return false;
        }

        protected async Task<TaskRunner<TActionParam>> FindFreeRunner()
        {
            foreach (var r in _runners)
            {
                if (await r.LockBusy())
                    return r;
            }
            return null;
        }
    }

    /******************************/

    public class TaskRunnerCoordinator<T, TActionParam> : TaskRunnerCoordinator<TActionParam>
    {
        public TaskRunnerCoordinator(int maxParallel, int minDelayMilliseconds) : base(maxParallel, minDelayMilliseconds, true)
        {
            for (var i = 0; i < _maxParallel; i++)
            {
                _runners.Add(new TaskRunner<TActionParam, T>(i, minDelayMilliseconds));
            }
        }

        public override TaskInfo<TActionParam, T> GetCompletedTaskInfo(int id)
        {
            if (!_completedTasks.ContainsKey(id))
                return null;
            return (TaskInfo<TActionParam, T>)_completedTasks[id];
        }

        public int Enqueue(Func<TActionParam, T> t, TActionParam arg)
        {
            if (_waitingTasks)
                throw new InvalidOperationException("Cannot enqueue new tasks if a waiting operation is in place.");
            var n = Interlocked.Increment(ref _lastTaskId);
            var ti = new TaskInfo<TActionParam, T>() { Id = n, Method = t, Argument = arg };
            _qtasks.Enqueue(ti);
            return n;
        }
    }
}
