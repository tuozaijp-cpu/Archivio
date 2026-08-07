using System;
using System.Threading.Tasks;

namespace Archivio.Helpers
{
    public static class DispatcherHelper
    {
        public static async Task RunOnUIThreadAsync(Action action)
        {
            if (App.MainWindow is null)
            {
                action();
                return;
            }

            var dispatcher = App.MainWindow.DispatcherQueue;
            if (dispatcher.HasThreadAccess)
            {
                action();
                return;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool enqueued = dispatcher.TryEnqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            if (!enqueued)
            {
                throw new InvalidOperationException("Failed to enqueue work on the UI thread.");
            }

            await tcs.Task;
        }

        public static async Task RunOnUIThreadAsync(Func<Task> action)
        {
            if (App.MainWindow is null)
            {
                await action();
                return;
            }

            var dispatcher = App.MainWindow.DispatcherQueue;
            if (dispatcher.HasThreadAccess)
            {
                await action();
                return;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool enqueued = dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            if (!enqueued)
            {
                throw new InvalidOperationException("Failed to enqueue work on the UI thread.");
            }

            await tcs.Task;
        }
    }
}
