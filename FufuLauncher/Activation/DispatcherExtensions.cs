using Microsoft.UI.Dispatching;

namespace FufuLauncher.Activation;

public static class DispatcherExtensions
{

    public static Task EnqueueAsync(this DispatcherQueue dispatcher, Func<Task> callback)
    {
        var tcs = new TaskCompletionSource();
        dispatcher.TryEnqueue(async () =>
        {
            try
            {
                await callback();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    public static Task<T> EnqueueAsync<T>(this DispatcherQueue dispatcher, Func<Task<T>> callback)
    {
        var tcs = new TaskCompletionSource<T>();
        dispatcher.TryEnqueue(async () =>
        {
            try
            {
                T result = await callback();
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    public static Task EnqueueAsync(this DispatcherQueue dispatcher, Action callback)
    {
        var tcs = new TaskCompletionSource();
        dispatcher.TryEnqueue(() =>
        {
            try
            {
                callback();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }
}