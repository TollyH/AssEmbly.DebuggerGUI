using System.IO;
using System.Windows.Threading;

namespace AssEmbly.DebuggerGUI
{
    public class BackgroundRunner(Processor processor, Dispatcher callbackDispatcher, Stream stdout, Stream stdin)
    {
        public delegate void ExecutionBreakCallback(bool halt);
        public delegate void ExceptionCallback(Exception exception);

        public Processor DebuggingProcessor { get; } = processor;
        public Dispatcher CallbackDispatcher { get; } = callbackDispatcher;

        private Thread? executionThread;

        /// <summary>
        /// Execute the next instruction in the processor.
        /// </summary>
        /// <returns><see langword="true"/> if execution started, or <see langword="false"/> if the runner is already busy.</returns>
        public bool ExecuteSingleInstruction(ExecutionBreakCallback breakCallback, ExceptionCallback exceptionCallback,
            CancellationToken cancellationToken)
        {
            if (executionThread is not null)
            {
                // Already busy
                return false;
            }

            executionThread = new Thread(() =>
            {
                try
                {
                    bool halt = DebuggingProcessor.Execute(false, stdout, stdin, false);
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        CallbackDispatcher.Invoke(() => breakCallback(halt));
                    }
                }
                catch (Exception exc)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        CallbackDispatcher.Invoke(() => exceptionCallback(exc));
                    }
                }
                executionThread = null;
            });
            executionThread.Start();
            return true;
        }
    }
}
