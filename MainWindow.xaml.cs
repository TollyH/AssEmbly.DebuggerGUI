using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace AssEmbly.DebuggerGUI
{
    public enum RunningState
    {
        Stopped,
        Paused,
        Running,
        AwaitingInput
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IDisposable
    {
        public Processor? DebuggingProcessor { get; private set; }

        private readonly HashSet<IBreakpoint> breakpoints = new();

        private string? lastOpenedPath;

        private CancellationTokenSource cancellationTokenSource = new();

        private BackgroundRunner? processorRunner;

        private readonly TextBlock[] registerValueBlocks;
        private readonly TextBlock[] registerValueExtraBlocks;
        private readonly Dictionary<StatusFlags, TextBlock> statusFlagBlocks;

        private readonly VirtualConsoleOutputStream consoleOutput;
        private readonly VirtualConsoleInputStream consoleInput;

        private readonly DisassemblerOptions disassemblerOptions = new(false, false, true, true, true);

        private Dictionary<ulong, (string Line, List<ulong> References)> disassembledLines = new();
        private List<Range> disassembledAddresses = new();

        private readonly FontFamily codeFont = new("Cascadia Code");

        private const double lineHeight = 18;

        public MainWindow()
        {
            InitializeComponent();

            registerValueBlocks = new TextBlock[16]
            {
                rpoValue, rsoValue, rsbValue, rsfValue, rrvValue, rfpValue,
                rg0Value, rg1Value, rg2Value, rg3Value, rg4Value, rg5Value,
                rg6Value, rg7Value, rg8Value, rg9Value
            };
            registerValueExtraBlocks = new TextBlock[16]
            {
                rpoValueExtra, rsoValueExtra, rsbValueExtra, rsfValueExtra, rrvValueExtra, rfpValueExtra,
                rg0ValueExtra, rg1ValueExtra, rg2ValueExtra, rg3ValueExtra, rg4ValueExtra, rg5ValueExtra,
                rg6ValueExtra, rg7ValueExtra, rg8ValueExtra, rg9ValueExtra
            };
            statusFlagBlocks = new Dictionary<StatusFlags, TextBlock>()
            {
                { StatusFlags.Zero, zeroFlagText },
                { StatusFlags.Carry, carryFlagText },
                { StatusFlags.FileEnd, fileEndFlagText },
                { StatusFlags.Sign, signFlagText },
                { StatusFlags.Overflow, overflowFlagText },
                { StatusFlags.AutoEcho, autoEchoFlagText }
            };

            consoleOutput = new VirtualConsoleOutputStream(consoleOutputBlock, Dispatcher);
            consoleInput = new VirtualConsoleInputStream(consoleInputBox, Dispatcher);
        }

        ~MainWindow()
        {
            Dispose();
        }

        public void LoadExecutable(string path)
        {
            AAPFile executable;
            try
            {
                executable = new AAPFile(File.ReadAllBytes(path));
            }
            catch (Exception exc)
            {
                DialogPopup popup = new(exc.Message, "Error loading executable", DialogPopup.ErrorIcon)
                {
                    Owner = this
                };
                popup.ShowDialog();
                return;
            }

            Environment.CurrentDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;

            if (!ulong.TryParse(memorySizeInput.Text, out ulong memorySize))
            {
                DialogPopup popup = new("Entered memory size is not valid.", "Error creating processor", DialogPopup.ErrorIcon)
                {
                    Owner = this
                };
                popup.ShowDialog();
                return;
            }

            UnloadExecutable();

            try
            {
                DebuggingProcessor = new Processor(memorySize, executable.EntryPoint,
                    forceV1.IsChecked ?? false, mapStack.IsChecked ?? true, autoEcho.IsChecked ?? false);
                DebuggingProcessor.LoadProgram(executable.Program);
                processorRunner = new BackgroundRunner(DebuggingProcessor, Dispatcher, consoleOutput, consoleInput);
                UpdateRunningState(RunningState.Paused);
            }
            catch (Exception exc)
            {
                DialogPopup popup = new(exc.Message, "Error creating processor", DialogPopup.ErrorIcon)
                {
                    Owner = this
                };
                popup.ShowDialog();
                return;
            }

            consoleOutputBlock.Text = "";
            consoleInputBox.Text = "";

            lastOpenedPath = path;
            executablePathText.Text = path;
            UpdateAllInformation();
            ReloadDisassembly();
            DisassembleFromProgramOffset(executable.EntryPoint);
        }

        private void ReloadDisassemblyAfterPosition(int start)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            ReadOnlySpan<byte> memory = DebuggingProcessor.Memory;
            while (start < DebuggingProcessor.Memory.Length)
            {
                (string line, ulong additionalOffset, List<ulong> references, _) =
                    Disassembler.DisassembleInstruction(memory[start..], disassemblerOptions, false);

                disassembledLines[(ulong)start] = (line, references);
                disassembledAddresses.Add(new Range(start, start + (long)additionalOffset));

                start += (int)additionalOffset;
            }

            programScroll.Maximum = disassembledLines.Count;

            UpdateDisassemblyView();
        }

        public void ReloadDisassembly()
        {
            disassembledLines.Clear();
            disassembledAddresses.Clear();

            ReloadDisassemblyAfterPosition(0);
        }

        public void ScrollToProgramOffset(ulong offset)
        {
            int closestIndex = 0;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < disassembledAddresses.Count; i++)
            {
                int distance = (int)Math.Abs(disassembledAddresses[i].Start - (long)offset);
                if (distance < bestDistance)
                {
                    closestIndex = i;
                    bestDistance = distance;
                }
                else if (distance > bestDistance)
                {
                    // We're getting further away and disassembledAddresses is always in order, so we can stop here.
                    break;
                }
            }
            if (closestIndex < programScroll.Value || closestIndex >= programScroll.Value + programScroll.ViewportSize - 1)
            {
                // Desired item is out of view - scroll it to center (the ScrollBar will clamp the value for us)
                programScroll.Value = closestIndex - (programScroll.ViewportSize / 2);
            }
        }

        public void DisassembleFromProgramOffset(ulong offset)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            if (disassembledLines.ContainsKey(offset))
            {
                // The given offset was already considered to be the start of an instruction,
                // no further disassembly is required.
                return;
            }

            // Remove any conflicting disassembled lines
            disassembledAddresses = disassembledAddresses.Where(a => a.End <= (long)offset).ToList();
            if (disassembledAddresses.Count > 0)
            {
                ulong maxStartAddress = (ulong)disassembledAddresses.Select(a => a.Start).Max();
                disassembledLines = disassembledLines.Where(kv => kv.Key <= maxStartAddress).ToDictionary(kv => kv.Key, kv => kv.Value);
            }
            else
            {
                disassembledLines = new Dictionary<ulong, (string Line, List<ulong> References)>();
            }

            // Fill space between offset and last valid instruction with %DAT
            for (long i = disassembledAddresses.Select(a => a.End).ToList().MaxOrDefault(0); i < (long)offset; i++)
            {
                disassembledAddresses.Add(new Range(i, i + 1));
                disassembledLines[(ulong)i] = ($"%DAT 0x{DebuggingProcessor.Memory[i]:X2}", new List<ulong>());
            }

            // Re-disassemble everything after the given offset
            ReloadDisassemblyAfterPosition((int)offset);

            UpdateDisassemblyView();
        }

        public void BreakExecution()
        {
            cancellationTokenSource.Cancel();
            cancellationTokenSource = new CancellationTokenSource();
        }

        public void UnloadExecutable()
        {
            DebuggingProcessor = null;
            processorRunner = null;

            BreakExecution();
            breakpoints.Clear();

            UpdateRunningState(RunningState.Stopped);
            executablePathText.Text = "No executable loaded";
        }

        public void UpdateAllInformation()
        {
            UpdateRegistersView();
            UpdateDisassemblyView();
            UpdateBreakpointListView();
        }

        public void UpdateRegistersView()
        {
            if (DebuggingProcessor is null)
            {
                return;
            }
            foreach (Register register in Enum.GetValues<Register>())
            {
                TextBlock block = registerValueBlocks[(int)register];
                TextBlock blockExtra = registerValueExtraBlocks[(int)register];

                ulong value = DebuggingProcessor.Registers[(int)register];
                string oldText = block.Text;
                string newText = value.ToString("X16");
                block.Text = newText;

                // Find most fitting alternate format for value
                double floatingValue = BitConverter.UInt64BitsToDouble(value);
                if (Math.Abs(floatingValue) is >= 0.0000000000000001 and <= ulong.MaxValue)
                {
                    blockExtra.Text = floatingValue.ToString(CultureInfo.InvariantCulture);
                }
                else if ((value & Processor.SignBit) != 0)
                {
                    blockExtra.Text = ((long)value).ToString();
                }
                // >= ' ' and <= '~'
                else if (value is >= 32 and <= 126)
                {
                    blockExtra.Text = $"'{((char)value).EscapeCharacter()}'";
                }
                else
                {
                    blockExtra.Text = value.ToString();
                }

                SolidColorBrush textColour = oldText == newText ? Brushes.White : Brushes.LightCoral;
                block.Foreground = textColour;
                blockExtra.Foreground = textColour;
            }

            foreach (StatusFlags flag in Enum.GetValues<StatusFlags>())
            {
                if (!statusFlagBlocks.TryGetValue(flag, out TextBlock? block))
                {
                    continue;
                }

                char bit = (DebuggingProcessor.Registers[(int)Register.rsf] & (ulong)flag) != 0 ? '1' : '0';
                string oldText = block.Text;
                string newText = $"{flag}: {bit}";
                block.Text = newText;

                SolidColorBrush textColour = oldText == newText ? Brushes.White : Brushes.LightCoral;
                block.Foreground = textColour;
            }
        }

        public void UpdateDisassemblyView()
        {
            int startAddressIndex = (int)programScroll.Value;
            for (int i = 0; i < programCodePanel.Children.Count; i++)
            {
                if (startAddressIndex + i >= disassembledAddresses.Count)
                {
                    programBreakpointsPanel.Children[i].Visibility = Visibility.Collapsed;
                    programLinesPanel.Children[i].Visibility = Visibility.Collapsed;
                    programCodePanel.Children[i].Visibility = Visibility.Collapsed;
                }
                else
                {
                    programBreakpointsPanel.Children[i].Visibility = Visibility.Visible;
                    programLinesPanel.Children[i].Visibility = Visibility.Visible;
                    programCodePanel.Children[i].Visibility = Visibility.Visible;

                    Range addressRange = disassembledAddresses[startAddressIndex + i];

                    BreakpointButton breakpointButton = (BreakpointButton)programBreakpointsPanel.Children[i];
                    breakpointButton.Address = (ulong)addressRange.Start;
                    breakpointButton.IsChecked = breakpoints.Contains(new RegisterValueBreakpoint(Register.rpo, (ulong)addressRange.Start));

                    TextBlock lineBlock = (TextBlock)programLinesPanel.Children[i];
                    lineBlock.Text = addressRange.Start.ToString("X16");
                    lineBlock.Foreground = (ulong)addressRange.Start == DebuggingProcessor?.Registers[(int)Register.rpo]
                        ? Brushes.LightCoral
                        : Brushes.White;

                    // TODO: Show referenced label names
                    // TODO: Syntax highlighting
                    ((TextBlock)programCodePanel.Children[i]).Text = disassembledLines[(ulong)addressRange.Start].Line;
                }
            }
        }

        public void UpdateBreakpointListView()
        {
            breakpointListAddresses.Children.Clear();
            breakpointListSourceLines.Children.Clear();
            foreach (RegisterValueBreakpoint breakpoint in breakpoints.OfType<RegisterValueBreakpoint>().OrderBy(b => b.TargetValue))
            {
                ContextMenus.BreakpointListContextMenu contextMenu = new(breakpoint);
                contextMenu.BreakpointRemoved += ContextMenu_BreakpointRemoved;
                contextMenu.BreakpointAdded += ContextMenu_BreakpointAdded;

                breakpointListAddresses.Children.Add(new TextBlock()
                {
                    Text = breakpoint.TargetValue.ToString("X16"),
                    Foreground = breakpoint.TargetValue == DebuggingProcessor?.Registers[(int)Register.rpo]
                        ? Brushes.LightCoral
                        : Brushes.White,
                    FontSize = 14,
                    FontFamily = codeFont,
                    Margin = new Thickness(5, 1, 5, 0),
                    ContextMenu = contextMenu
                });
                breakpointListSourceLines.Children.Add(new TextBlock()
                {
                    Text = disassembledLines.GetValueOrDefault(breakpoint.TargetValue).Line,
                    Foreground = Brushes.White,
                    FontSize = 14,
                    FontFamily = codeFont,
                    Margin = new Thickness(5, 1, 5, 0),
                    ContextMenu = contextMenu
                });
            }
        }

        public void ReloadDisassemblyView()
        {
            programBreakpointsPanel.Children.Clear();
            programLinesPanel.Children.Clear();
            programCodePanel.Children.Clear();

            int lineCount = (int)(programCodePanel.ActualHeight / lineHeight);
            for (int i = 0; i < lineCount; i++)
            {
                BreakpointButton breakpointButton = new()
                {
                    Height = lineHeight,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                breakpointButton.Checked += BreakpointButton_Checked;
                breakpointButton.Unchecked += BreakpointButton_Unchecked;
                programBreakpointsPanel.Children.Add(breakpointButton);
                programLinesPanel.Children.Add(new TextBlock()
                {
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0),
                    FontFamily = codeFont,
                    Height = lineHeight,
                    FontSize = 14
                });
                programCodePanel.Children.Add(new TextBlock()
                {
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 0, 0),
                    FontFamily = codeFont,
                    Height = lineHeight,
                    FontSize = 14
                });
            }

            programScroll.ViewportSize = lineCount;

            UpdateDisassemblyView();
        }

        public void UpdateRunningState(RunningState state)
        {
            switch (state)
            {
                case RunningState.Stopped:
                    runningStatusText.Foreground = Brushes.Red;
                    runningStatusText.Text = "Stopped";
                    break;
                case RunningState.Paused:
                    runningStatusText.Foreground = Brushes.Orange;
                    runningStatusText.Text = "Paused";
                    break;
                case RunningState.Running:
                    runningStatusText.Foreground = Brushes.LawnGreen;
                    runningStatusText.Text = "Running";
                    break;
                case RunningState.AwaitingInput:
                    runningStatusText.Foreground = Brushes.Cyan;
                    runningStatusText.Text = "Awaiting Input";
                    break;
                default:
                    runningStatusText.Foreground = Brushes.White;
                    runningStatusText.Text = "Unknown";
                    break;
            }
        }

        public void Dispose()
        {
            cancellationTokenSource.Cancel();
            cancellationTokenSource.Dispose();
            GC.SuppressFinalize(this);
        }

        private void OnBreak(BackgroundRunner sender, bool halt)
        {
            if (DebuggingProcessor is null || !ReferenceEquals(processorRunner, sender))
            {
                return;
            }

            DisassembleFromProgramOffset(DebuggingProcessor.Registers[(int)Register.rpo]);
            ScrollToProgramOffset(DebuggingProcessor.Registers[(int)Register.rpo]);
            UpdateRunningState(consoleInput.EmptyReadAttempt ? RunningState.AwaitingInput : RunningState.Paused);
            UpdateAllInformation();
            if (halt)
            {
                UnloadExecutable();
            }
        }

        private void OnException(BackgroundRunner sender, Exception exception)
        {
            if (!ReferenceEquals(processorRunner, sender))
            {
                return;
            }

            DialogPopup popup = new($"{exception.GetType()}: {exception.Message}",
                "AssEmbly Exception", DialogPopup.ErrorIcon)
            {
                Owner = this
            };
            popup.ShowDialog();

            UpdateAllInformation();
            UnloadExecutable();
        }

        private long? AskHexadecimalNumber(string message, string title)
        {
            DialogPopup popup = new(message, title, DialogPopup.QuestionIcon, true)
            {
                Owner = this
            };

            if (!(popup.ShowDialog() ?? false))
            {
                return null;
            }

            try
            {
                return Convert.ToInt64(popup.InputText, 16);
            }
            catch (Exception exception)
            {
                popup = new DialogPopup("The entered value was invalid.\n" + exception.Message,
                    "Invalid Value", DialogPopup.ErrorIcon)
                {
                    Owner = this
                };
                popup.ShowDialog();
                return null;
            }
        }

        private void AboutItem_Click(object sender, RoutedEventArgs e)
        {
            AboutWindow window = new()
            {
                Owner = this
            };
            window.ShowDialog();
        }

        private void OpenItem_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new()
            {
                CheckFileExists = true,
                CheckPathExists = true,
                Filter = "Assembled AssEmbly Programs (*.aap)|*.aap",
                Title = "Open Executable"
            };
            if (dialog.ShowDialog(this) ?? false)
            {
                LoadExecutable(dialog.FileName);
            }
        }

        private void StepInItem_Click(object sender, RoutedEventArgs e)
        {
            if (processorRunner is null)
            {
                return;
            }
            if (processorRunner.ExecuteSingleInstruction(OnBreak, OnException, cancellationTokenSource.Token))
            {
                UpdateRunningState(RunningState.Running);
            }
        }

        private void StopItem_Click(object sender, RoutedEventArgs e)
        {
            UnloadExecutable();
        }

        private void RestartItem_Click(object sender, RoutedEventArgs e)
        {
            if (lastOpenedPath is not null)
            {
                LoadExecutable(lastOpenedPath);
            }
        }

        private void consoleOutputBlock_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            consoleScroll.ScrollToBottom();
        }

        private void programScroll_Scroll(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e)
        {
            UpdateDisassemblyView();
        }

        private void ProgramGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ReloadDisassemblyView();
        }

        private void ProgramGrid_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            programScroll.Value -= Math.CopySign(programScroll.ActualHeight / lineHeight / 4, e.Delta);
            UpdateDisassemblyView();
        }

        private void BreakItem_Click(object sender, RoutedEventArgs e)
        {
            BreakExecution();
        }

        private void ResumeItem_Click(object sender, RoutedEventArgs e)
        {
            if (processorRunner is null)
            {
                return;
            }
            if (processorRunner.ExecuteUntilBreak(OnBreak, OnException, breakpoints, cancellationTokenSource.Token))
            {
                UpdateRunningState(RunningState.Running);
            }
        }

        private void BreakpointButton_Unchecked(object sender, RoutedEventArgs e)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }
            _ = breakpoints.Remove(new RegisterValueBreakpoint(Register.rpo, ((BreakpointButton)sender).Address));
        }

        private void BreakpointButton_Checked(object sender, RoutedEventArgs e)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }
            _ = breakpoints.Add(new RegisterValueBreakpoint(Register.rpo, ((BreakpointButton)sender).Address));
        }

        private void BreakpointItem_Click(object sender, RoutedEventArgs e)
        {
            if (DebuggingProcessor is null || (processorRunner?.IsBusy ?? true))
            {
                return;
            }
            _ = breakpoints.Add(new RegisterValueBreakpoint(Register.rpo, DebuggingProcessor.Registers[(int)Register.rpo]));
            UpdateDisassemblyView();
            UpdateBreakpointListView();
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (mainTabControl.SelectedIndex == 1)
            {
                // Breakpoint tab selected
                UpdateBreakpointListView();
            }
        }

        private void ContextMenu_BreakpointRemoved(ContextMenus.BreakpointListContextMenu sender)
        {
            _ = breakpoints.Remove(sender.Breakpoint);
            UpdateBreakpointListView();
            UpdateDisassemblyView();
        }

        private void ContextMenu_BreakpointAdded(ContextMenus.BreakpointListContextMenu sender)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            long? value = AskHexadecimalNumber("Enter the address to break on in hexadecimal", "New Breakpoint");
            if (value is null)
            {
                return;
            }
            _ = breakpoints.Add(new RegisterValueBreakpoint(Register.rpo, (ulong)value.Value));
            UpdateBreakpointListView();
            UpdateDisassemblyView();
        }
    }
}
