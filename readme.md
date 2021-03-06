# Throttler

Throttler is a component that can run tasks in parallel, with a configured concurrency and minimum delay between task executions.

## Features

- Configurable maximum  parallelism between 1 and 10 tasks.
- Configurable delay between task executions between 100ms and 60 seconds.

## Structure
Throttler uses just 3 classes for doing the work:
- The tasks coordinator, which handles tasks allocation to runners and delays between dispatching them.
- The task runners, which are wrappers around the client's real action/functions. These keep track of the completion of the target method and sets the task result internally.
- TaskInfo class, which hold information about the target methods to be executed.
 
## Use
Just reference the project, or copy its classes to your own one.
Then, create an instance of TaskRunnerCoordinator with the desired max concurrency and min delay between tasks.

Use the constructor below if the target method are actions (no return value). The generic parameter <string> is the type of the argument passed to the task's target method.

```
# I.e: Run no more than 3 parallel tasks every 3 seconds.
var tr = new Throttler.TaskRunnerCoordinator<string>(3, 3000); 
```

Use the constructor below if the target method are funcs (have return value). The SECOND generic parameter <string> is the type of the task's return value.
```
var tr = new Throttler.TaskRunnerCoordinator<string, string>(3, 3000); 
```

After the coordinator is created, start enqueuing tasks to it:
```sh
# simulate a task that should call an external API with a name of a person as a parameter
# define the list of persons:
var list = new List<string>();
list.Add("juan");
list.Add("pedro");
list.Add("maria");
list.Add("jose");
list.Add("jesus");
list.Add("matias");
list.Add("elena");
list.Add("magda");
list.Add("felipe");
list.Add("tadeo");

# define a list that will hold the IDs of the created tasks
var taskids = new List<int>();
foreach (var s in list)
{
    taskids.Add(tr.Enqueue(new Func<string,string>(CallExternalApiFake), s));
}
 
string CallExternalApiFake(string name)
{
    var r = Random.Shared.Next(3, 8) * 100;
    Throttler.Logger.Log($"Inside {nameof(CallExternalApiFake)}. Args={name}. Delay of task={r}ms.");
    Thread.Sleep(r); // simulate an api call with the delay

    // this simulates an uncontrolled exception, that the throttler must manage
    if (name == "matias")
        throw new ApplicationException("TEST EXCEPTION!");
    return $"{name} is OK!";
}
```

`tr.Enqueue` merely creates a reference to tasks that will be executed, but they're aren't executed yet.

To begin processing the tasks with the concurrency/delay defined in the constructor of TaskRunnerCoordinator, run:

```sh
await tr.ProcessTasks();
```

Then, you may want to wait for the tasks to complete.

**Note:** If the target method is an action (no return value), then you may opt not to wait for completion, unless you're terminating a process or something like that.
```
await tr.WaitForTasksToComplete();
```

Finally, if you need the return value of the tasks after they have completed, get them with GetCompletedTaskInfo:

```sh
var sb = new System.Text.StringBuilder();
foreach (var tid in taskids)
{
    var ti = tr.GetCompletedTaskInfo(tid);
    sb.Append(Environment.NewLine);
    sb.Append($"Task #{tid}, Args={ti.Argument}, Status={ti.Status}");
    if (ti.Status == TaskStatus.Faulted)
    {
        sb.Append(", Errors=");
        foreach (var e in ti.Exceptions)
        {
            sb.Append(Environment.NewLine);
            sb.Append(e.Message);
        }
    }
    else
        sb.Append($",Result={ti.TaskResult}");
}
Throttler.Logger.Log(sb.ToString());
```

## Requirements/dependencies
- .NET Core 6 (but should be able to run in lower version, and also in .NET Framework too)

## Pending
It should be nice to have a method in the tasks coordinator to cancel all pending/running tasks, maybe I'll add it in a future version.

## License

MIT

