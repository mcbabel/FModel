using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace FModel.Framework.Ui;

/// <summary>
/// Helpers to emulate WPF's synchronous modal APIs (ShowDialog, file pickers, ...) on top of
/// Avalonia's async APIs by pumping a nested dispatcher loop until the task completes.
/// </summary>
public static class UiThread
{
    public static T WaitFor<T>(Func<Task<T>> func)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.Invoke(() => WaitFor(func));

        var task = func();
        Pump(task);
        return task.GetAwaiter().GetResult();
    }

    public static void WaitFor(Func<Task> func)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Invoke(() => WaitFor(func));
            return;
        }

        var task = func();
        Pump(task);
        task.GetAwaiter().GetResult();
    }

    private static void Pump(Task task)
    {
        if (task.IsCompleted)
            return;

        using var cts = new CancellationTokenSource();
        task.ContinueWith(_ =>
        {
            cts.Cancel();
            Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Send); // wake up the nested loop
        }, TaskScheduler.Default);
        Dispatcher.UIThread.MainLoop(cts.Token);
    }
}
