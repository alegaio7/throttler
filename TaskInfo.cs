using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Throttler
{
    public class TaskInfo<TActionParam>
    {
        public int Id { get; set; }
        public virtual Action<TActionParam> Method { get; set; }
        public TActionParam Argument { get; set; }
        public List<Exception> Exceptions = new();
        public TaskStatus Status { get; set; } = TaskStatus.Created;
    }

    public class TaskInfo<TActionParam, T> : TaskInfo<TActionParam>
    {
        public new Func<TActionParam, T> Method { get; set; }
        public T TaskResult { get; set; }
    }
}
