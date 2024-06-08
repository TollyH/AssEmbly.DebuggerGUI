using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
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

    public enum JumpArrowStyle
    {
        Unconditional,
        UnconditionalWillJump,
        ConditionalSatisfied,
        ConditionalSatisfiedWillJump,
        ConditionalUnsatisfied
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IDisposable
    {
        public Processor? DebuggingProcessor { get; private set; }

        public ulong SelectedMemoryAddress { get; private set; } = 0;

        private readonly HashSet<IBreakpoint> breakpoints = new();
        private readonly Dictionary<string, ulong> labels = new();
        private readonly Dictionary<ulong, string> savedAddresses = new();

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

        private readonly Dictionary<ulong, int> currentlyRenderedInstructions = new();

        private readonly FontFamily codeFont = new("Consolas");

        private const double lineHeight = 14;

        private const double jumpArrowOffset = lineHeight / 2;
        private const double jumpArrowSpacing = 10;
        private const double jumpArrowMinSize = 20;
        private const double jumpArrowHeadSize = 3;

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
                ShowErrorDialog(exc.Message, "Error loading executable");
                return;
            }

            Environment.CurrentDirectory = System.IO.Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;

            LoadExecutable(executable.Program, executable.EntryPoint);

            lastOpenedPath = path;
            executablePathText.Text = path;

            DisassembleFromProgramOffset(executable.EntryPoint);
        }

        public void LoadRawExecutable(string path)
        {
            byte[] executable;
            try
            {
                executable = File.ReadAllBytes(path);
            }
            catch (Exception exc)
            {
                ShowErrorDialog(exc.Message, "Error loading executable");
                return;
            }

            Environment.CurrentDirectory = System.IO.Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;

            LoadExecutable(executable, 0);

            lastOpenedPath = path;
            executablePathText.Text = path;
        }

        public void LoadExecutable(byte[] program, ulong entryPoint)
        {
            if (!ulong.TryParse(memorySizeInput.Text, out ulong memorySize))
            {
                ShowErrorDialog("Entered memory size is not valid.", "Error creating processor");
                return;
            }

            UnloadExecutable();

            try
            {
                DebuggingProcessor = new Processor(memorySize, entryPoint,
                    forceV1.IsChecked ?? false, mapStack.IsChecked ?? true, autoEcho.IsChecked ?? false);
                DebuggingProcessor.LoadProgram(program);
                processorRunner = new BackgroundRunner(DebuggingProcessor, Dispatcher, consoleOutput, consoleInput);
                UpdateRunningState(RunningState.Paused);
            }
            catch (Exception exc)
            {
                ShowErrorDialog(exc.Message, "Error creating processor");
                return;
            }

            consoleOutputBlock.Text = "";
            consoleInputBox.Text = "";

            UpdateAllInformation();
            ReloadDisassembly();
            ScrollToProgramOffset(entryPoint);
        }

        public void LoadADI(string path)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            try
            {
                DebugInfo.DebugInfoFile infoFile = DebugInfo.ParseDebugInfoFile(File.ReadAllText(path));

                // Convert Dict<ulong, string[]> to Dict<string, ulong>,
                // where the same address can now appear multiple times, mapped against unique names
                foreach ((ulong address, string[] names) in infoFile.AddressLabels)
                {
                    foreach (string name in names)
                    {
                        labels[name] = address;
                    }
                }

                disassembledLines.Clear();
                disassembledAddresses.Clear();

                foreach ((ulong address, _) in infoFile.AssembledInstructions.OrderBy(kv => kv.Key))
                {
                    DisassembleFromProgramOffset(address);
                }
            }
            catch (Exception exc)
            {
                ShowErrorDialog(exc.Message, "Error loading debug information file");
                return;
            }

            UpdateAllInformation();
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
            UpdateDisassemblyView();
        }

        public void DisassembleFromProgramOffset(ulong offset, bool force = false)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            if (!force && disassembledLines.ContainsKey(offset))
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
            labels.Clear();
            savedAddresses.Clear();

            SelectedMemoryAddress = 0;

            UpdateRunningState(RunningState.Stopped);
            executablePathText.Text = "No executable loaded";
        }

        public void UpdateAllInformation()
        {
            UpdateRegistersView();
            UpdateDisassemblyView();
            UpdateBreakpointListView();
            UpdateLabelListView();
            UpdateSavedAddressListView();
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

                block.ToolTip = $"{value} = {(long)value} = 0x{value:X} = 0b{value:B} = {floatingValue}";
                if (value is >= 32 and <= 126)
                {
                    block.ToolTip += $" = '{((char)value).EscapeCharacter()}'";
                }
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
            if (DebuggingProcessor is null)
            {
                return;
            }

            currentlyRenderedInstructions.Clear();
            programJumpArrowCanvas.Children.Clear();

            int startAddressIndex = (int)programScroll.Value;
            for (int i = 0; i < programCodePanel.Children.Count; i++)
            {
                if (startAddressIndex + i >= disassembledAddresses.Count)
                {
                    programBytesPanel.Children[i].Visibility = Visibility.Collapsed;
                    programBreakpointsPanel.Children[i].Visibility = Visibility.Collapsed;
                    programLinesPanel.Children[i].Visibility = Visibility.Collapsed;
                    programLabelsPanel.Children[i].Visibility = Visibility.Collapsed;
                    programCodePanel.Children[i].Visibility = Visibility.Collapsed;
                }
                else
                {
                    Range addressRange = disassembledAddresses[startAddressIndex + i];

                    currentlyRenderedInstructions[(ulong)addressRange.Start] = i;

                    ContextMenus.ProgramContextMenu contextMenu = new((ulong)addressRange.Start);
                    contextMenu.AddressSaved += ContextMenu_AddressSaved;
                    contextMenu.LabelAdded += ContextMenu_LabelAddedFromProgram;
                    contextMenu.Jumped += ContextMenu_Jumped;
                    contextMenu.BreakpointToggled += ContextMenu_BreakpointToggled;
                    contextMenu.Edited += ContextMenu_Edited;

                    TextBlock bytesBlock = (TextBlock)programBytesPanel.Children[i];
                    bytesBlock.Visibility = Visibility.Visible;
                    bytesBlock.Text = "";
                    foreach (byte programByte in DebuggingProcessor!.Memory.AsSpan((int)addressRange.Start, (int)addressRange.Length))
                    {
                        bytesBlock.Text += $"{programByte:X2} ";
                    }
                    bytesBlock.ContextMenu = contextMenu;

                    BreakpointButton breakpointButton = (BreakpointButton)programBreakpointsPanel.Children[i];
                    breakpointButton.Visibility = Visibility.Visible;
                    breakpointButton.Address = (ulong)addressRange.Start;
                    breakpointButton.IsChecked = breakpoints.Contains(new RegisterValueBreakpoint(Register.rpo, (ulong)addressRange.Start));
                    breakpointButton.ContextMenu = contextMenu;

                    TextBlock lineBlock = (TextBlock)programLinesPanel.Children[i];
                    lineBlock.Visibility = Visibility.Visible;
                    lineBlock.Text = addressRange.Start.ToString("X16");
                    lineBlock.Foreground = (ulong)addressRange.Start == DebuggingProcessor?.Registers[(int)Register.rpo]
                        ? Brushes.LightCoral
                        : Brushes.White;
                    lineBlock.ContextMenu = contextMenu;

                    TextBlock labelsBlock = (TextBlock)programLabelsPanel.Children[i];
                    labelsBlock.Visibility = Visibility.Visible;
                    labelsBlock.Text = "";
                    foreach ((string name, _) in labels.Where(l => l.Value == (ulong)addressRange.Start))
                    {
                        labelsBlock.Text += $":{name} ";
                    }
                    labelsBlock.ContextMenu = contextMenu;

                    // TODO: Syntax highlighting
                    TextBlock codeBlock = (TextBlock)programCodePanel.Children[i];
                    codeBlock.Visibility = Visibility.Visible;
                    codeBlock.Text = disassembledLines[(ulong)addressRange.Start].Line;
                    foreach (ulong referencedAddress in disassembledLines[(ulong)addressRange.Start].References)
                    {
                        foreach (string label in labels.Where(kv => kv.Value == referencedAddress).Select(kv => kv.Key))
                        {
                            codeBlock.Text += $"  ; 0x{referencedAddress:X} -> :{label}";
                        }
                    }
                    codeBlock.ContextMenu = contextMenu;
                }
            }

            UpdateJumpArrows();
        }

        private void UpdateJumpArrows()
        {
            int jumpArrowIndex = 0;

            int startAddressIndex = (int)programScroll.Value;
            for (int i = 0; i < programCodePanel.Children.Count && startAddressIndex + i < disassembledAddresses.Count; i++)
            {
                Range addressRange = disassembledAddresses[startAddressIndex + i];

                bool currentInstruction = (ulong)addressRange.Start == DebuggingProcessor?.Registers[(int)Register.rpo];

                ulong offset = (ulong)addressRange.Start;
                // Don't parse an opcode if there isn't enough memory remaining for it
                if (DebuggingProcessor!.Memory[offset] != Opcode.FullyQualifiedMarker
                    || offset <= (ulong)DebuggingProcessor.Memory.Length - 3)
                {
                    Opcode instructionOpcode = Opcode.ParseBytes(DebuggingProcessor.Memory, ref offset);
                    offset++;
                    // Don't parse the operand if there isn't enough memory remaining for it
                    if (offset <= (ulong)DebuggingProcessor.Memory.Length - 8)
                    {
                        ulong targetAddress = BinaryPrimitives.ReadUInt64LittleEndian(DebuggingProcessor.Memory.AsSpan((int)offset));
                        if (JumpInstructions.UnconditionalJumps.Contains(instructionOpcode))
                        {
                            DrawJumpArrow((ulong)addressRange.Start, targetAddress,
                                currentInstruction ? JumpArrowStyle.UnconditionalWillJump : JumpArrowStyle.Unconditional, jumpArrowIndex);
                            jumpArrowIndex++;
                        }
                        else if (JumpInstructions.ConditionalJumps.TryGetValue(instructionOpcode,
                            out (StatusFlags Flags, StatusFlags FlagMask)[]? conditions))
                        {
                            bool conditionMet = conditions.Any(c =>
                                ((StatusFlags)DebuggingProcessor.Registers[(int)Register.rsf] & c.FlagMask) == c.Flags);
                            DrawJumpArrow((ulong)addressRange.Start, targetAddress,
                                conditionMet
                                    ? currentInstruction
                                        ? JumpArrowStyle.ConditionalSatisfiedWillJump
                                        : JumpArrowStyle.ConditionalSatisfied
                                    : JumpArrowStyle.ConditionalUnsatisfied,
                                jumpArrowIndex);
                            jumpArrowIndex++;
                        }
                    }
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
                    FontFamily = codeFont,
                    Margin = new Thickness(5, 1, 5, 0),
                    ContextMenu = contextMenu
                });
                breakpointListSourceLines.Children.Add(new TextBlock()
                {
                    Text = disassembledLines.GetValueOrDefault(breakpoint.TargetValue).Line,
                    Foreground = Brushes.White,
                    FontFamily = codeFont,
                    Margin = new Thickness(5, 1, 5, 0),
                    ContextMenu = contextMenu
                });
            }
        }

        public void UpdateLabelListView()
        {
            labelsListNames.Children.Clear();
            labelsListAddresses.Children.Clear();
            foreach ((string name, ulong address) in labels.OrderBy(l => l.Value))
            {
                ContextMenus.LabelListContextMenu contextMenu = new(name);
                contextMenu.LabelRemoved += ContextMenu_LabelRemoved;
                contextMenu.LabelAdded += ContextMenu_LabelAdded;
                contextMenu.LabelDisassembling += ContextMenu_LabelDisassembling;

                labelsListNames.Children.Add(new TextBlock()
                {
                    Text = name,
                    Foreground = Brushes.White,
                    FontFamily = codeFont,
                    Margin = new Thickness(5, 1, 5, 0),
                    ContextMenu = contextMenu
                });
                labelsListAddresses.Children.Add(new TextBlock()
                {
                    Text = address.ToString("X16"),
                    Foreground = address == DebuggingProcessor?.Registers[(int)Register.rpo]
                        ? Brushes.LightCoral
                        : Brushes.White,
                    FontFamily = codeFont,
                    Margin = new Thickness(5, 1, 5, 0),
                    ContextMenu = contextMenu
                });
            }
        }

        public void UpdateSavedAddressListView()
        {
            savedAddressListAddresses.Children.Clear();
            savedAddressListDescriptions.Children.Clear();
            foreach ((ulong address, string description) in savedAddresses.OrderBy(l => l.Key))
            {
                ContextMenus.SavedAddressListContextMenu contextMenu = new(address);
                contextMenu.AddressRemoved += ContextMenu_AddressRemoved;
                contextMenu.AddressAdded += ContextMenu_AddressAdded;

                savedAddressListAddresses.Children.Add(new TextBlock()
                {
                    Text = address.ToString("X16"),
                    Foreground = address == DebuggingProcessor?.Registers[(int)Register.rpo]
                        ? Brushes.LightCoral
                        : Brushes.White,
                    FontFamily = codeFont,
                    Margin = new Thickness(5, 1, 5, 0),
                    ContextMenu = contextMenu
                });
                savedAddressListDescriptions.Children.Add(new TextBlock()
                {
                    Text = description,
                    Foreground = Brushes.White,
                    FontFamily = codeFont,
                    Margin = new Thickness(5, 1, 5, 0),
                    ContextMenu = contextMenu
                });
            }
        }

        public void ReloadDisassemblyView()
        {
            programBytesPanel.Children.Clear();
            programBreakpointsPanel.Children.Clear();
            programLinesPanel.Children.Clear();
            programLabelsPanel.Children.Clear();
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
                programBytesPanel.Children.Add(new TextBlock()
                {
                    Foreground = Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 5, 0),
                    FontFamily = codeFont,
                    Height = lineHeight,
                });
                programLinesPanel.Children.Add(new TextBlock()
                {
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = codeFont,
                    Height = lineHeight,
                });
                programLabelsPanel.Children.Add(new TextBlock()
                {
                    Foreground = Brushes.LightBlue,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 5, 0),
                    FontFamily = codeFont,
                    Height = lineHeight,
                });
                programCodePanel.Children.Add(new TextBlock()
                {
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 0, 0),
                    FontFamily = codeFont,
                    Height = lineHeight,
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

        private void CreateLabelPromptName(ulong address)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            string? name = AskString("Enter the name of the new label", "New Label");
            if (name is null)
            {
                return;
            }

            if (!labels.TryAdd(name, address))
            {
                ShowErrorDialog($"A label with the name \"{name}\" already exists", "Label Creation Failed");
                return;
            }

            UpdateLabelListView();
        }

        private void SaveAddressPromptName(ulong address)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            string? name = AskString("Enter a description for the saved address (optional)", "Save Address");
            if (name is null)
            {
                return;
            }

            savedAddresses[address] = name;

            UpdateSavedAddressListView();
        }

        private void PromptInstructionPatch(ulong address)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            string currentLine;
            ulong instructionSize;
            if (disassembledLines.TryGetValue(address, out (string Line, List<ulong> References) line))
            {
                currentLine = line.Line;
                instructionSize = (ulong)disassembledAddresses.First(a => a.Start == (long)address).Length;
            }
            else
            {
                (currentLine, instructionSize, _, _) = Disassembler.DisassembleInstruction(
                    DebuggingProcessor.Memory.AsSpan((int)address), disassemblerOptions, false);
            }

            int proceedingNops = 0;
            while ((int)(address + instructionSize) + proceedingNops < DebuggingProcessor.Memory.Length
                && DebuggingProcessor.Memory[(int)(address + instructionSize) + proceedingNops] == 0x01)
            {
                proceedingNops++;
            }

            PatchDialog dialog = new(currentLine, (int)instructionSize + proceedingNops, (int)instructionSize, labels)
            {
                Owner = this
            };
            if (!(dialog.ShowDialog() ?? false) || dialog.AssemblyResult == PatchDialog.ResultType.Fail)
            {
                return;
            }

            dialog.AssembledBytes.CopyTo(DebuggingProcessor.Memory, (long)address);
            for (int i = dialog.AssembledBytes.Length; i < (int)instructionSize; i++)
            {
                DebuggingProcessor.Memory[(int)address + i] = 0x01;  // NOP
            }

            DisassembleFromProgramOffset(address, true);
            UpdateAllInformation();
        }

        private void DrawJumpArrow(ulong startAddress, ulong targetAddress, JumpArrowStyle style, int indentationIndex)
        {
            if (!currentlyRenderedInstructions.TryGetValue(startAddress, out int startIndex))
            {
                return;
            }

            bool upwards = targetAddress < startAddress;

            bool drawEndLine;
            double targetY;
            if (currentlyRenderedInstructions.TryGetValue(targetAddress, out int targetIndex))
            {
                drawEndLine = true;
                targetY = targetIndex * lineHeight + jumpArrowOffset;
            }
            else
            {
                drawEndLine = false;
                targetY = upwards ? 0 : programJumpArrowCanvas.ActualHeight;
            }

            double startY = startIndex * lineHeight + jumpArrowOffset;
            double innerX = programJumpArrowCanvas.ActualWidth - 2;
            double outerX = innerX - jumpArrowMinSize - (indentationIndex * jumpArrowSpacing);

            double arrowThickness = style is JumpArrowStyle.UnconditionalWillJump or JumpArrowStyle.ConditionalSatisfiedWillJump ? 3 : 1;
            SolidColorBrush arrowColor = style is
                JumpArrowStyle.Unconditional
                or JumpArrowStyle.UnconditionalWillJump
                or JumpArrowStyle.ConditionalUnsatisfied
                    ? Brushes.DarkGray
                    : Brushes.Red;
            DoubleCollection arrowDashArray = style is
                JumpArrowStyle.ConditionalSatisfied
                or JumpArrowStyle.ConditionalSatisfiedWillJump
                or JumpArrowStyle.ConditionalUnsatisfied
                    ? new DoubleCollection([2, 2])
                    : new DoubleCollection();

            programJumpArrowCanvas.Children.Add(new Line()
            {
                X1 = innerX,
                Y1 = startY,
                X2 = outerX,
                Y2 = startY,
                StrokeThickness = arrowThickness,
                Stroke = arrowColor,
                StrokeDashArray = arrowDashArray,
                SnapsToDevicePixels = true
            });
            programJumpArrowCanvas.Children.Add(new Line()
            {
                X1 = outerX,
                Y1 = startY,
                X2 = outerX,
                Y2 = targetY,
                StrokeThickness = arrowThickness,
                Stroke = arrowColor,
                StrokeDashArray = arrowDashArray,
                SnapsToDevicePixels = true
            });
            if (drawEndLine)
            {
                programJumpArrowCanvas.Children.Add(new Line()
                {
                    X1 = outerX,
                    Y1 = targetY,
                    X2 = innerX,
                    Y2 = targetY,
                    StrokeThickness = arrowThickness,
                    Stroke = arrowColor,
                    StrokeDashArray = arrowDashArray,
                    SnapsToDevicePixels = true
                });
                // Arrow line head
                programJumpArrowCanvas.Children.Add(new Line()
                {
                    X1 = innerX,
                    Y1 = targetY + 1,
                    X2 = innerX - jumpArrowHeadSize,
                    Y2 = targetY - jumpArrowHeadSize + 1,
                    StrokeThickness = arrowThickness,
                    Stroke = arrowColor,
                    SnapsToDevicePixels = true
                });
                programJumpArrowCanvas.Children.Add(new Line()
                {
                    X1 = innerX,
                    Y1 = targetY + 1,
                    X2 = innerX - jumpArrowHeadSize,
                    Y2 = targetY + jumpArrowHeadSize + 1,
                    StrokeThickness = arrowThickness,
                    Stroke = arrowColor,
                    SnapsToDevicePixels = true
                });
            }
            else
            {
                // Arrow line head
                programJumpArrowCanvas.Children.Add(new Line()
                {
                    X1 = outerX + 1,
                    Y1 = targetY,
                    X2 = outerX - jumpArrowHeadSize + 1,
                    Y2 = targetY + (upwards ? jumpArrowHeadSize : -jumpArrowHeadSize),
                    StrokeThickness = arrowThickness,
                    Stroke = arrowColor,
                    SnapsToDevicePixels = true
                });
                programJumpArrowCanvas.Children.Add(new Line()
                {
                    X1 = outerX + 1,
                    Y1 = targetY,
                    X2 = outerX + jumpArrowHeadSize + 1,
                    Y2 = targetY + (upwards ? jumpArrowHeadSize : -jumpArrowHeadSize),
                    StrokeThickness = arrowThickness,
                    Stroke = arrowColor,
                    SnapsToDevicePixels = true
                });
            }
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

            ShowErrorDialog($"{exception.GetType()}: {exception.Message}", "AssEmbly Exception");

            UpdateAllInformation();
            UnloadExecutable();
        }

        private void ShowErrorDialog(string message, string title)
        {
            DialogPopup popup = new(message, title, DialogPopup.ErrorIcon)
            {
                Owner = this
            };
            popup.ShowDialog();
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
                ShowErrorDialog("The entered value was invalid.\n" + exception.Message, "Invalid Value");
                return null;
            }
        }

        private string? AskString(string message, string title)
        {
            DialogPopup popup = new(message, title, DialogPopup.QuestionIcon, true)
            {
                Owner = this
            };

            if (!(popup.ShowDialog() ?? false))
            {
                return null;
            }

            return popup.InputText;
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
                Filter = "Assembled AssEmbly Programs (*.aap)|*.aap|All file types|*",
                Title = "Open Executable"
            };
            if (dialog.ShowDialog(this) ?? false)
            {
                LoadExecutable(dialog.FileName);
            }
        }

        private void OpenRawItem_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new()
            {
                CheckFileExists = true,
                CheckPathExists = true,
                Filter = "All file types|*",
                Title = "Open Raw Executable"
            };
            if (dialog.ShowDialog(this) ?? false)
            {
                LoadRawExecutable(dialog.FileName);
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
            UpdateAllInformation();
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            switch (mainTabControl.SelectedIndex)
            {
                case 0:  // Program tab
                    UpdateDisassemblyView();
                    break;
                case 1:  // Breakpoint tab
                    UpdateBreakpointListView();
                    break;
                case 3:  // Labels tab
                    UpdateLabelListView();
                    break;
            }
        }

        private void MemoryTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            switch (mainTabControl.SelectedIndex)
            {
                case 2:  // Saved addresses tab
                    UpdateSavedAddressListView();
                    break;
            }
        }

        private void ContextMenu_BreakpointRemoved(ContextMenus.BreakpointListContextMenu sender)
        {
            _ = breakpoints.Remove(sender.Breakpoint);
            UpdateAllInformation();
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
            UpdateAllInformation();
        }

        private void ADIItem_Click(object sender, RoutedEventArgs e)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            OpenFileDialog dialog = new()
            {
                CheckFileExists = true,
                CheckPathExists = true,
                Filter = "AssEmbly Debug Information (*.adi)|*.adi|All file types|*",
                Title = "Open Debug Information File"
            };
            if (dialog.ShowDialog(this) ?? false)
            {
                LoadADI(dialog.FileName);
            }
        }

        private void ContextMenu_LabelAdded(ContextMenus.LabelListContextMenu sender)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            long? value = AskHexadecimalNumber("Enter the address to add label to in hexadecimal", "New Label");
            if (value is null)
            {
                return;
            }

            CreateLabelPromptName((ulong)value);
        }

        private void ContextMenu_LabelRemoved(ContextMenus.LabelListContextMenu sender)
        {
            _ = labels.Remove(sender.LabelName);
            UpdateAllInformation();
        }

        private void LabelItem_Click(object sender, RoutedEventArgs e)
        {
            if (DebuggingProcessor is null || (processorRunner?.IsBusy ?? true))
            {
                return;
            }

            CreateLabelPromptName(DebuggingProcessor.Registers[(int)Register.rpo]);

            UpdateAllInformation();
        }

        private void ContextMenu_LabelDisassembling(ContextMenus.LabelListContextMenu sender)
        {
            if (labels.TryGetValue(sender.LabelName, out ulong address))
            {
                DisassembleFromProgramOffset(address, true);
            }
        }

        private void DisassemblePartialItem_Click(object sender, RoutedEventArgs e)
        {
            if (DebuggingProcessor is null || (processorRunner?.IsBusy ?? true))
            {
                return;
            }

            DisassembleFromProgramOffset(DebuggingProcessor.Registers[(int)Register.rpo], true);
        }

        private void DisassembleFullItem_Click(object sender, RoutedEventArgs e)
        {
            if (DebuggingProcessor is null || (processorRunner?.IsBusy ?? true))
            {
                return;
            }

            ReloadDisassembly();
        }

        private void StepOverItem_Click(object sender, RoutedEventArgs e)
        {
            if (processorRunner is null)
            {
                return;
            }
            if (processorRunner.ExecuteOverFunction(OnBreak, OnException, cancellationTokenSource.Token))
            {
                UpdateRunningState(RunningState.Running);
            }
        }

        private void StepOutItem_Click(object sender, RoutedEventArgs e)
        {
            if (processorRunner is null)
            {
                return;
            }
            if (processorRunner.ExecuteUntilReturn(OnBreak, OnException, cancellationTokenSource.Token))
            {
                UpdateRunningState(RunningState.Running);
            }
        }

        private void SaveAddressItem_Click(object sender, RoutedEventArgs e)
        {
            if (DebuggingProcessor is null || (processorRunner?.IsBusy ?? true))
            {
                return;
            }

            SaveAddressPromptName(SelectedMemoryAddress);

            UpdateAllInformation();
        }

        private void ContextMenu_AddressAdded(ContextMenus.SavedAddressListContextMenu sender)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            long? value = AskHexadecimalNumber("Enter the address to save in hexadecimal", "Save Address");
            if (value is null)
            {
                return;
            }

            SaveAddressPromptName((ulong)value);
        }

        private void ContextMenu_AddressRemoved(ContextMenus.SavedAddressListContextMenu sender)
        {
            _ = savedAddresses.Remove(sender.Address);
            UpdateAllInformation();
        }

        private void ContextMenu_BreakpointToggled(ContextMenus.ProgramContextMenu sender)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            RegisterValueBreakpoint breakpoint = new(Register.rpo, sender.Address);
            if (!breakpoints.Add(breakpoint))
            {
                breakpoints.Remove(breakpoint);
            }
            UpdateAllInformation();
        }

        private void ContextMenu_Jumped(ContextMenus.ProgramContextMenu sender)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            DebuggingProcessor.Registers[(int)Register.rpo] = sender.Address;

            UpdateAllInformation();
        }

        private void ContextMenu_LabelAddedFromProgram(ContextMenus.ProgramContextMenu sender)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            CreateLabelPromptName(sender.Address);

            UpdateAllInformation();
        }

        private void ContextMenu_AddressSaved(ContextMenus.ProgramContextMenu sender)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            SaveAddressPromptName(sender.Address);

            UpdateAllInformation();
        }

        private void PatchItem_Click(object sender, RoutedEventArgs e)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            PromptInstructionPatch(DebuggingProcessor.Registers[(int)Register.rpo]);

            UpdateAllInformation();
        }

        private void ContextMenu_Edited(ContextMenus.ProgramContextMenu sender)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            PromptInstructionPatch(sender.Address);

            UpdateAllInformation();
        }

        private void programJumpArrowCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateDisassemblyView();
        }
    }
}
