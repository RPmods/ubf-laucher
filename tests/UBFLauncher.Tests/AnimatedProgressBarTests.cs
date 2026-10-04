using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UBFLauncher.Controls;
using Xunit;

namespace UBFLauncher.Tests;

public sealed class AnimatedProgressBarTests
{
    [Fact]
    public Task MarqueeAndIndeterminateAnimationsRemainUsable()
    {
        return RunOnStaAsync(() =>
        {
            var app = new App();
            app.InitializeComponent();

            Exception? dispatcherError = null;
            var dispatcher = Dispatcher.CurrentDispatcher;
            DispatcherUnhandledExceptionEventHandler exceptionHandler = (_, args) =>
            {
                dispatcherError = args.Exception;
                args.Handled = true;
            };
            dispatcher.UnhandledException += exceptionHandler;

            var progress = new AnimatedProgressBar
            {
                Width = 360,
                Height = 8,
                Style = (Style)app.FindResource("OperationProgressBar")
            };
            var window = new Window
            {
                Content = progress,
                Width = 400,
                Height = 80,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };

            try
            {
                window.Show();
                PumpDispatcher(dispatcher, 120);

                var shimmer = Assert.IsAssignableFrom<FrameworkElement>(
                    progress.Template.FindName("ProgressShimmer", progress));
                var shimmerStart = shimmer.Margin.Left;
                PumpDispatcher(dispatcher, 160);
                Assert.NotEqual(shimmerStart, shimmer.Margin.Left);

                progress.IsIndeterminate = true;
                var indeterminate = Assert.IsAssignableFrom<FrameworkElement>(
                    progress.Template.FindName("IndeterminateIndicator", progress));
                PumpDispatcher(dispatcher, 80);
                var indeterminateStart = indeterminate.Margin.Left;
                PumpDispatcher(dispatcher, 160);
                Assert.NotEqual(indeterminateStart, indeterminate.Margin.Left);

                progress.IsIndeterminate = false;
                PumpDispatcher(dispatcher, 40);
            }
            finally
            {
                window.Close();
                dispatcher.UnhandledException -= exceptionHandler;
                app.Shutdown();
            }

            Assert.Null(dispatcherError);
            return true;
        });
    }

    private static Task<T> RunOnStaAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(action()); }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void PumpDispatcher(Dispatcher dispatcher, int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(milliseconds)
        };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }
}
