using System.IO;
using System.Windows.Threading;

namespace AssEmbly.DebuggerGUI
{
    public class BackgroundRunner(Processor processor, Dispatcher callbackDispatcher, Stream stdout, Stream stdin)
    {
        public delegate void ExecutionBreakCallback(BackgroundRunner sender, bool halt);
        public delegate void InstructionExecutionCallback(BackgroundRunner sender);
        public delegate void ExceptionCallback(BackgroundRunner sender, Exception exception);

        public Processor DebuggingProcessor { get; } = processor;
        public Dispatcher CallbackDispatcher { get; } = callbackDispatcher;

        public bool IsBusy => executionThread is not null;

        private Thread? executionThread;

        /// <summary>
        /// Execute the next instructions in the processor, until either a HLT instruction or break condition is reached.
        /// </summary>
        /// <returns><see langword="true"/> if execution started, or <see langword="false"/> if the runner is already busy.</returns>
        public bool ExecuteUntilBreak(ExecutionBreakCallback breakCallback, ExceptionCallback exceptionCallback,
            InstructionExecutionCallback executionCallback, IEnumerable<IBreakpoint> breakConditions, CancellationToken cancellationToken)
        {
            if (IsBusy)
            {
                return false;
            }

            IBreakpoint[] breakpoints = breakConditions.ToArray();

            executionThread = new Thread(() =>
            {
                try
                {
                    bool halt = false;
                    while (!halt && !cancellationToken.IsCancellationRequested)
                    {
                        halt = DebuggingProcessor.Execute(false, stdout, stdin, false);
                        CallbackDispatcher.Invoke(() => executionCallback(this));
                        // This check shouldn't run for the first instruction
                        // so that we don't break on a breakpoint we just continued from
                        if (breakpoints.Any(b => b.ShouldBreak(DebuggingProcessor)))
                        {
                            break;
                        }
                    }
                    CallbackDispatcher.Invoke(() => breakCallback(this, halt));
                }
                catch (Exception exc)
                {
                    CallbackDispatcher.Invoke(() => exceptionCallback(this, exc));
                }
                executionThread = null;
            });
            executionThread.Start();
            return true;
        }

        /// <summary>
        /// Execute the next instructions in the processor, until the next instruction inside the current subroutine or higher is reached.
        /// </summary>
        /// <returns><see langword="true"/> if execution started, or <see langword="false"/> if the runner is already busy.</returns>
        public bool ExecuteOverFunction(ExecutionBreakCallback breakCallback, ExceptionCallback exceptionCallback,
            InstructionExecutionCallback executionCallback, CancellationToken cancellationToken)
        {
            if (IsBusy)
            {
                return false;
            }

            executionThread = new Thread(() =>
            {
                try
                {
                    ulong startStackBase = DebuggingProcessor.Registers[(int)Register.rsb];
                    bool halt = false;
                    while (!halt && !cancellationToken.IsCancellationRequested)
                    {
                        halt = DebuggingProcessor.Execute(false, stdout, stdin, false);
                        CallbackDispatcher.Invoke(() => executionCallback(this));
                        // This check shouldn't run for the first instruction
                        // so that we don't break on the instruction we just continued from
                        if (DebuggingProcessor.Registers[(int)Register.rsb] >= startStackBase)
                        {
                            break;
                        }
                    }
                    CallbackDispatcher.Invoke(() => breakCallback(this, halt));
                }
                catch (Exception exc)
                {
                    CallbackDispatcher.Invoke(() => exceptionCallback(this, exc));
                }
                executionThread = null;
            });
            executionThread.Start();
            return true;
        }

        /// <summary>
        /// Execute the next instructions in the processor, until the next return instruction for the current subroutine or higher is reached.
        /// </summary>
        /// <returns><see langword="true"/> if execution started, or <see langword="false"/> if the runner is already busy.</returns>
        public bool ExecuteUntilReturn(ExecutionBreakCallback breakCallback, ExceptionCallback exceptionCallback,
            InstructionExecutionCallback executionCallback, CancellationToken cancellationToken)
        {
            if (IsBusy)
            {
                return false;
            }

            executionThread = new Thread(() =>
            {
                try
                {
                    ulong startStackBase = DebuggingProcessor.Registers[(int)Register.rsb];
                    bool halt = false;
                    while (!halt && !cancellationToken.IsCancellationRequested)
                    {
                        halt = DebuggingProcessor.Execute(false, stdout, stdin, false);
                        CallbackDispatcher.Invoke(() => executionCallback(this));
                        // This check shouldn't run for the first instruction
                        // so that we don't break on the instruction we just continued from
                        if (DebuggingProcessor.Registers[(int)Register.rsb] >= startStackBase
                            // Is the next instruction a return instruction?
                            && DebuggingProcessor.Memory[DebuggingProcessor.Registers[(int)Register.rpo]] is 0xBA or 0xBB or 0xBC or 0xBD or 0xBE)
                        {
                            break;
                        }
                    }
                    CallbackDispatcher.Invoke(() => breakCallback(this, halt));
                }
                catch (Exception exc)
                {
                    CallbackDispatcher.Invoke(() => exceptionCallback(this, exc));
                }
                executionThread = null;
            });
            executionThread.Start();
            return true;
        }

        /// <summary>
        /// Execute the next instruction in the processor.
        /// </summary>
        /// <returns><see langword="true"/> if execution started, or <see langword="false"/> if the runner is already busy.</returns>
        public bool ExecuteSingleInstruction(ExecutionBreakCallback breakCallback, ExceptionCallback exceptionCallback,
            InstructionExecutionCallback executionCallback, CancellationToken cancellationToken)
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
                    CallbackDispatcher.Invoke(() => executionCallback(this));
                    CallbackDispatcher.Invoke(() => breakCallback(this, halt));
                }
                catch (Exception exc)
                {
                    CallbackDispatcher.Invoke(() => exceptionCallback(this, exc));
                }
                executionThread = null;
            });
            executionThread.Start();
            return true;
        }
    }
}
