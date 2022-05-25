using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Throttler
{
    public class TaskRunner<TActionParam>
    {
        protected Task _task;
        protected DateTime? _lastRun;
        protected SemaphoreSlim _lock = new(1);
        protected LockStateEnum _lockState;
                
        protected int _mindelay;
        protected int _id;
        protected TaskInfo<TActionParam> _taskInfo;
        protected bool _busyWarningIssued;

        public enum LockStateEnum
        { 
            Free,
            LockSet,
            Busy,
            Completed
        }

        public TaskRunner(int id, int minDelayMilliseconds) {
            _id = id;
            _mindelay = minDelayMilliseconds;
        }

        public int Id { get { return _id; } }

        public TaskInfo<TActionParam> AssignedTask { get { return _taskInfo; } }

        public LockStateEnum LockState {  get { return _lockState; } }

        public async Task<bool> LockBusy()
        {
            await _lock.WaitAsync();
            try
            {
                if (_lockState != LockStateEnum.Free)
                    return false;
                if (_lastRun.HasValue && (DateTime.Now - _lastRun.Value).TotalMilliseconds < _mindelay)
                {
                    if (!_busyWarningIssued)
                    {
                        _busyWarningIssued = true;
                        Logger.Log($"runner {Id} is free, but it didn't pass enough time after the last run task.");
                    }
                    return false;
                }
                _busyWarningIssued = false;
                _lockState = LockStateEnum.LockSet;
                return true;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool> Unlock()
        {
            await _lock.WaitAsync();
            try
            {
                if (_lockState != LockStateEnum.LockSet && _lockState != LockStateEnum.Completed) // release lock only if previously acquired, but task isn't running yet
                    return false;
                _lockState = LockStateEnum.Free;
                _taskInfo = null;
                return true;
            }
            finally
            {
                _lock.Release();
            }
        }

        public DateTime? LastRun
        {
            get {
                return _lastRun;
            }
        }

        public virtual async Task RunTask(TaskInfo<TActionParam> taskInfo)
        {
            await _lock.WaitAsync();
            try
            {
                Logger.Log($"Task runner creating task #{taskInfo.Id}. Args={taskInfo.Argument}");
                if (_lockState != LockStateEnum.LockSet)
                    throw new InvalidOperationException("Task runner is not locked");
                if (_task != null)
                    throw new InvalidOperationException("Task runner has a task already assigned"); // should not happen
                _taskInfo = taskInfo;
                _lockState = LockStateEnum.Busy;
                _lastRun = DateTime.Now;
                _task = TaskCreator(taskInfo);
                // don't await on this on purpose.. continuation is used to set task status, but should be executed async
                _task.ContinueWith(async (t) => {
                    Logger.Log($"Task #{taskInfo.Id} completed. Args={taskInfo.Argument}. Setting result: {t.Status}");
                    await SetResult(t, _taskInfo);
                });
            }
            catch (Exception)
            {
                if (_task != null)
                {
                    _task.Dispose();
                    _task = null;
                }
                throw;
            }
            finally { 
                _lock.Release();
            }
        }

        protected virtual Task TaskCreator(object taskinfo)
        {
            TaskInfo<TActionParam> ti = (TaskInfo<TActionParam>)taskinfo;
            return Task.Run(() => {
                Logger.Log($"About to invoke method for task #{ti.Id}. Args={ti.Argument}");
                ti.Method.Invoke(ti.Argument);
            });
        }

        protected virtual async Task SetResult(Task t, TaskInfo<TActionParam> ti, bool disposeTask = true) {
            await _lock.WaitAsync();
            try
            {
                switch (t.Status)
                {
                    case TaskStatus.Canceled:
                        ti.Status = TaskStatus.Canceled;
                        break;
                    case TaskStatus.Faulted:
                        ti.Status = TaskStatus.Faulted;
                        ti.Exceptions.AddRange(t.Exception.InnerExceptions);
                        break;
                    case TaskStatus.RanToCompletion:
                        ti.Status = TaskStatus.RanToCompletion;
                        break;
                }
            }
            finally
            {
                _lockState = LockStateEnum.Completed;
                if (disposeTask && _task != null)
                {
                    _task.Dispose();
                    _task = null;
                }
                _lock.Release();
            }
        }
    }

    /*************************/

    public class TaskRunner<TActionParam, T> : TaskRunner<TActionParam>
    {
        public TaskRunner(int id, int minDelayMilliseconds) : base(id, minDelayMilliseconds) { }

        protected override Task TaskCreator(object taskinfo)
        {
            TaskInfo<TActionParam, T> ti = (TaskInfo<TActionParam, T>)taskinfo;
            return Task.Run(() =>
            {
                return ti.Method.Invoke(ti.Argument);
            });
        }

        protected override async Task SetResult(Task t, TaskInfo<TActionParam> ti, bool disposeTask = true)
        {
            await base.SetResult(t, ti, false);
            await _lock.WaitAsync();
            try
            {
                switch (t.Status)
                {
                    case TaskStatus.RanToCompletion:
                        ((TaskInfo<TActionParam, T>)ti).TaskResult = ((Task<T>)t).Result;
                        break;
                }
            }
            finally
            {
                _lockState = LockStateEnum.Completed;
                if (disposeTask && _task != null)
                {
                    _task.Dispose();
                    _task = null;
                }
                _lock.Release();
            }
        }
    }
}
