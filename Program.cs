
// use this if the target methods are actions (no return value)
//var tr = new Throttler.TaskRunnerCoordinator<string>(5, 2000); 

// use this if the target methods are funcs (have return value)
var tr = new Throttler.TaskRunnerCoordinator<string, string>(3, 3000); 

// simulation of a task that should send emails to a list of persons
// define the list of persons:
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

// define list of IDs of tasks that will be created.
var taskids = new List<int>();
foreach (var s in list)
{
    //taskids.Add(tr.Enqueue(new Action<string>(SendFakeEmailTask), s)); // action version
    taskids.Add(tr.Enqueue(new Func<string,string>(GetFakeReturnValue), s)); // func version
}

// begin processing the tasks with the concurrenty/delay defined in the ctor of TaskRunnerCoordinator
await tr.ProcessTasks();

// wait for the tasks to complete
Throttler.Logger.Log("Waiting tasks for complete...");
await tr.WaitForTasksToComplete();

// show the results of each task
Throttler.Logger.Log("TASK RESULTS:");
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

Throttler.Logger.Log("Completed");
Console.ReadKey();

// A fake method that simulates an action (i.e. sending an email to a person),
// with a random delay to appear more real.
void SendFakeEmailTask(string name) {
    var r = Random.Shared.Next(3, 8) * 100;
    Throttler.Logger.Log($"Inside {nameof(SendFakeEmailTask)}. Args={name}. Delay of task={r}ms.");
    Thread.Sleep(r);

    // this simulates an uncontrolled exception, that the throttler must manage
    if (name == "matias")
        throw new Exception("TEST EXCEPTION!");
}

// A fake method that simulates a function with a return value
// (i.e. calling an external metered API and get a value), with a random delay to appear more real.
string GetFakeReturnValue(string name)
{
    var r = Random.Shared.Next(3, 8) * 100;
    Throttler.Logger.Log($"Inside {nameof(GetFakeReturnValue)}. Args={name}. Delay of task={r}ms.");
    Thread.Sleep(r);

    // this simulates an uncontrolled exception, that the throttler must manage
    if (name == "matias")
        throw new ApplicationException("TEST EXCEPTION!");
    return $"{name} is OK!";
}