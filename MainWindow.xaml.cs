﻿using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        private readonly StackPanel[] memoryBytePanels;
        private readonly StackPanel[] memoryAsciiPanels;

        private readonly VirtualConsoleOutputStream consoleOutput;
        private readonly VirtualConsoleInputStream consoleInput;

        private readonly DisassemblerOptions disassemblerOptions = new(false, false, true, true, true);

        private Dictionary<ulong, (string Line, List<ulong> References)> disassembledLines = new();
        private List<Range> disassembledAddresses = new();

        private readonly Dictionary<ulong, int> currentlyRenderedInstructions = new();
        private readonly Dictionary<ulong, int> currentMaxArrowIndentation = new();
        private readonly Dictionary<ulong, int> currentlyRenderedPointerArrows = new();

        private readonly FontFamily codeFont = new("Consolas");

        private readonly SyntaxHighlighting highlighter = new();

        private const double lineHeight = 14;

        private const double jumpArrowOffset = lineHeight / 2;
        private const double jumpArrowSpacing = 10;
        private const double jumpArrowMinSize = 21;
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

            memoryBytePanels = new StackPanel[16]
            {
                memoryBytesPanel0, memoryBytesPanel1, memoryBytesPanel2, memoryBytesPanel3,
                memoryBytesPanel4, memoryBytesPanel5, memoryBytesPanel6, memoryBytesPanel7,
                memoryBytesPanel8, memoryBytesPanel9, memoryBytesPanelA, memoryBytesPanelB,
                memoryBytesPanelC, memoryBytesPanelD, memoryBytesPanelE, memoryBytesPanelF
            };
            memoryAsciiPanels = new StackPanel[16]
            {
                memoryAsciiPanel0, memoryAsciiPanel1, memoryAsciiPanel2, memoryAsciiPanel3,
                memoryAsciiPanel4, memoryAsciiPanel5, memoryAsciiPanel6, memoryAsciiPanel7,
                memoryAsciiPanel8, memoryAsciiPanel9, memoryAsciiPanelA, memoryAsciiPanelB,
                memoryAsciiPanelC, memoryAsciiPanelD, memoryAsciiPanelE, memoryAsciiPanelF
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

            UpdateAllInformation();
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

            memoryScroll.Maximum = Math.Ceiling(DebuggingProcessor.Memory.Length / 16d);

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
        }

        public void ReloadDisassembly()
        {
            disassembledLines.Clear();
            disassembledAddresses.Clear();

            ReloadDisassemblyAfterPosition(0);

            UpdateDisassemblyView();
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

        public void ScrollAndSelectMemoryOffset(ulong offset)
        {
            int closestIndex = (int)offset / 16;
            if (closestIndex < memoryScroll.Value || closestIndex >= memoryScroll.Value + memoryScroll.ViewportSize - 1)
            {
                // Desired item is out of view - scroll it to center (the ScrollBar will clamp the value for us)
                memoryScroll.Value = closestIndex - (memoryScroll.ViewportSize / 2);
            }

            SelectedMemoryAddress = offset;

            UpdateMemoryView();
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
            UpdateMemoryRegionListView();
            UpdateMemoryView();
            UpdateHeapStatsView();
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

                    TextBlock bytesBlock = (TextBlock)programBytesPanel.Children[i];
                    bytesBlock.Visibility = Visibility.Visible;
                    bytesBlock.Text = "";
                    foreach (byte programByte in DebuggingProcessor!.Memory.AsSpan((int)addressRange.Start, (int)addressRange.Length))
                    {
                        bytesBlock.Text += $"{programByte:X2} ";
                    }
                    ((ContextMenus.ProgramContextMenu)bytesBlock.ContextMenu!).Address = (ulong)addressRange.Start;

                    BreakpointButton breakpointButton = (BreakpointButton)programBreakpointsPanel.Children[i];
                    breakpointButton.Visibility = Visibility.Visible;
                    breakpointButton.Address = (ulong)addressRange.Start;
                    breakpointButton.IsChecked = breakpoints.Contains(new RegisterValueBreakpoint(Register.rpo, (ulong)addressRange.Start));
                    ((ContextMenus.ProgramContextMenu)breakpointButton.ContextMenu!).Address = (ulong)addressRange.Start;

                    TextBlock lineBlock = (TextBlock)programLinesPanel.Children[i];
                    lineBlock.Visibility = Visibility.Visible;
                    lineBlock.Text = addressRange.Start.ToString("X16");
                    lineBlock.Foreground = (ulong)addressRange.Start == DebuggingProcessor?.Registers[(int)Register.rpo]
                        ? Brushes.LightCoral
                        : Brushes.White;
                    ((ContextMenus.ProgramContextMenu)lineBlock.ContextMenu!).Address = (ulong)addressRange.Start;

                    TextBlock labelsBlock = (TextBlock)programLabelsPanel.Children[i];
                    labelsBlock.Visibility = Visibility.Visible;
                    labelsBlock.Text = "";
                    foreach ((string name, _) in labels.Where(l => l.Value == (ulong)addressRange.Start))
                    {
                        labelsBlock.Text += $":{name} ";
                    }
                    ((ContextMenus.ProgramContextMenu)labelsBlock.ContextMenu!).Address = (ulong)addressRange.Start;

                    TextBlock codeBlock = (TextBlock)programCodePanel.Children[i];
                    codeBlock.Visibility = Visibility.Visible;
                    ((ContextMenus.ProgramContextMenu)codeBlock.ContextMenu!).Address = (ulong)addressRange.Start;

                    string line = disassembledLines[(ulong)addressRange.Start].Line;
                    foreach (ulong referencedAddress in disassembledLines[(ulong)addressRange.Start].References)
                    {
                        foreach (string label in labels.Where(kv => kv.Value == referencedAddress).Select(kv => kv.Key))
                        {
                            line += $"  ; 0x{referencedAddress:X} -> :{label}";
                        }
                    }
                    codeBlock.Inlines.Clear();
                    codeBlock.Inlines.AddRange(highlighter.HighlightLine(line));
                }
            }

            UpdateJumpArrows();
        }

        private void UpdateJumpArrows()
        {
            if (DebuggingProcessor is null || disassembledAddresses.Count == 0)
            {
                return;
            }

            currentMaxArrowIndentation.Clear();

            int startAddressIndex = (int)programScroll.Value;

            ulong minDisplayedAddress = (ulong)disassembledAddresses[startAddressIndex].Start;
            ulong maxDisplayedAddress = (ulong)disassembledAddresses[
                Math.Min(startAddressIndex + programCodePanel.Children.Count - 1, disassembledAddresses.Count - 1)].LastIndex;

            for (int i = 0; i < programCodePanel.Children.Count && startAddressIndex + i < disassembledAddresses.Count; i++)
            {
                Range addressRange = disassembledAddresses[startAddressIndex + i];

                bool currentInstruction = (ulong)addressRange.Start == DebuggingProcessor.Registers[(int)Register.rpo];

                ulong offset = (ulong)addressRange.Start;
                // Don't parse an opcode if there isn't enough memory remaining for it
                if (DebuggingProcessor.Memory[offset] != Opcode.FullyQualifiedMarker
                    || offset <= (ulong)DebuggingProcessor.Memory.Length - 3)
                {
                    Opcode instructionOpcode = Opcode.ParseBytes(DebuggingProcessor.Memory, ref offset);
                    offset++;
                    // Don't parse the operand if there isn't enough memory remaining for it
                    if (offset <= (ulong)DebuggingProcessor.Memory.Length - 8)
                    {
                        ulong targetAddress = BinaryPrimitives.ReadUInt64LittleEndian(DebuggingProcessor.Memory.AsSpan((int)offset));
                        bool arrowDrawn = false;
                        int jumpArrowIndex = 0;
                        if (JumpInstructions.UnconditionalJumps.Contains(instructionOpcode))
                        {
                            jumpArrowIndex = currentMaxArrowIndentation
                                .Where(kv => (ulong)addressRange.Start < targetAddress
                                        ? kv.Key >= (ulong)addressRange.Start && kv.Key <= targetAddress
                                        : kv.Key <= (ulong)addressRange.Start && kv.Key >= targetAddress)
                                .Select(kv => kv.Value).DefaultIfEmpty(0).Max();
                            DrawJumpArrow((ulong)addressRange.Start, targetAddress,
                                currentInstruction ? JumpArrowStyle.UnconditionalWillJump : JumpArrowStyle.Unconditional, jumpArrowIndex);
                            arrowDrawn = true;
                        }
                        else if (JumpInstructions.ConditionalJumps.TryGetValue(instructionOpcode,
                            out (StatusFlags Flags, StatusFlags FlagMask)[]? conditions))
                        {
                            jumpArrowIndex = currentMaxArrowIndentation
                                .Where(kv => (ulong)addressRange.Start < targetAddress
                                    ? kv.Key >= (ulong)addressRange.Start && kv.Key <= targetAddress
                                    : kv.Key <= (ulong)addressRange.Start && kv.Key >= targetAddress)
                                .Select(kv => kv.Value).DefaultIfEmpty(0).Max();
                            bool conditionMet = conditions.Any(c =>
                                ((StatusFlags)DebuggingProcessor.Registers[(int)Register.rsf] & c.FlagMask) == c.Flags);
                            DrawJumpArrow((ulong)addressRange.Start, targetAddress,
                                conditionMet
                                    ? currentInstruction
                                        ? JumpArrowStyle.ConditionalSatisfiedWillJump
                                        : JumpArrowStyle.ConditionalSatisfied
                                    : JumpArrowStyle.ConditionalUnsatisfied,
                                jumpArrowIndex);
                            arrowDrawn = true;
                        }

                        if (arrowDrawn)
                        {
                            jumpArrowIndex++;
                            for (ulong address = Math.Min((ulong)addressRange.Start, targetAddress);
                                address <= Math.Max((ulong)addressRange.Start, targetAddress); address++)
                            {
                                if (address > maxDisplayedAddress || address < minDisplayedAddress)
                                {
                                    break;
                                }
                                currentMaxArrowIndentation[address] = jumpArrowIndex;
                            }
                        }
                    }
                }
            }

            currentlyRenderedPointerArrows.Clear();

            for (int i = 0; i < DebuggingProcessor.Registers.Length; i++)
            {
                ulong value = DebuggingProcessor.Registers[i];
                if (currentlyRenderedInstructions.ContainsKey(value))
                {
                    DrawPointerArrow((Register)i, value, currentMaxArrowIndentation.GetValueOrDefault(value));
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
                contextMenu.ProgramScrolled += ContextMenu_ProgramScrolled;
                contextMenu.MemoryScrolled += ContextMenu_MemoryScrolled;

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
                ContextMenus.LabelListContextMenu contextMenu = new(name, address);
                contextMenu.LabelRemoved += ContextMenu_LabelRemoved;
                contextMenu.LabelAdded += ContextMenu_LabelAdded;
                contextMenu.LabelDisassembling += ContextMenu_LabelDisassembling;
                contextMenu.ProgramScrolled += ContextMenu_ProgramScrolled;
                contextMenu.MemoryScrolled += ContextMenu_MemoryScrolled;

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
                contextMenu.ProgramScrolled += ContextMenu_ProgramScrolled;
                contextMenu.MemoryScrolled += ContextMenu_MemoryScrolled;

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

        public void UpdateMemoryRegionListView()
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            regionListStartAddresses.Children.Clear();
            regionListEndAddresses.Children.Clear();
            regionListLengths.Children.Clear();
            regionListTypes.Children.Clear();
            foreach (Range region in DebuggingProcessor.MappedMemoryRanges)
            {
                ContextMenus.RegionListContextMenu contextMenu = new((ulong)region.Start);
                contextMenu.LabelAdded += ContextMenu_LabelAddedWithAddress;
                contextMenu.AddressSaved += ContextMenu_AddressSaved;
                contextMenu.ProgramScrolled += ContextMenu_ProgramScrolled;
                contextMenu.MemoryScrolled += ContextMenu_MemoryScrolled;

                regionListStartAddresses.Children.Add(new TextBlock()
                {
                    Text = region.Start.ToString("X16"),
                    Foreground = Brushes.White,
                    FontFamily = codeFont,
                    Margin = new Thickness(5, 1, 5, 0),
                    ContextMenu = contextMenu
                });
                regionListEndAddresses.Children.Add(new TextBlock()
                {
                    Text = region.LastIndex.ToString("X16"),
                    Foreground = Brushes.White,
                    FontFamily = codeFont,
                    Margin = new Thickness(5, 1, 5, 0),
                    ContextMenu = contextMenu
                });
                regionListLengths.Children.Add(new TextBlock()
                {
                    Text = region.Length.ToString("X16"),
                    Foreground = Brushes.White,
                    FontFamily = codeFont,
                    Margin = new Thickness(5, 1, 5, 0),
                    ContextMenu = contextMenu
                });
                regionListTypes.Children.Add(new TextBlock()
                {
                    Text = region.Start == 0
                        ? "Program"
                        : region.End == DebuggingProcessor.Memory.Length
                            ? "Stack"
                            : "Heap",
                    Foreground = Brushes.White,
                    FontFamily = codeFont,
                    Margin = new Thickness(5, 1, 5, 0),
                    ContextMenu = contextMenu
                });
            }
        }

        public void UpdateMemoryView()
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            int startAddressIndex = (int)memoryScroll.Value;
            for (int i = 0; i < memoryAddressPanel.Children.Count; i++)
            {
                int startAddress = (startAddressIndex + i) * 16;

                TextBlock addressBlock = (TextBlock)memoryAddressPanel.Children[i];
                if (startAddress >= DebuggingProcessor.Memory.Length)
                {
                    addressBlock.Visibility = Visibility.Hidden;
                }
                else
                {
                    addressBlock.Visibility = Visibility.Visible;
                    addressBlock.Text = startAddress.ToString("X16");
                }

                for (int addressOffset = 0; addressOffset < 16; addressOffset++)
                {
                    TextBlock dataBlock = (TextBlock)memoryBytePanels[addressOffset].Children[i];
                    TextBlock asciiBlock = (TextBlock)memoryAsciiPanels[addressOffset].Children[i];
                    int address = startAddress + addressOffset;
                    if (address >= DebuggingProcessor.Memory.Length)
                    {
                        dataBlock.Visibility = Visibility.Hidden;
                        asciiBlock.Visibility = Visibility.Hidden;
                    }
                    else
                    {
                        byte data = DebuggingProcessor.Memory[address];
                        SolidColorBrush? background = (ulong)address == SelectedMemoryAddress ? Brushes.Gray : null;

                        dataBlock.Visibility = Visibility.Visible;
                        dataBlock.Text = data.ToString("X2");
                        dataBlock.Background = background;
                        dataBlock.Tag = (ulong)address;
                        ((ContextMenus.MemoryContextMenu)dataBlock.ContextMenu!).Address = (ulong)address;

                        asciiBlock.Visibility = Visibility.Visible;
                        // >= ' ' and <= '~'
                        asciiBlock.Text = data is >= 32 and <= 126 ? ((char)data).ToString() : ".";
                        asciiBlock.Background = background;
                        asciiBlock.Tag = (ulong)address;
                        ((ContextMenus.MemoryContextMenu)asciiBlock.ContextMenu!).Address = (ulong)address;
                    }
                }
            }
        }

        public void UpdateHeapStatsView()
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            long memorySize = DebuggingProcessor.Memory.LongLength;
            IReadOnlyList<Range> mappedRanges = DebuggingProcessor.MappedMemoryRanges;
            long programSize = mappedRanges[0].Length;
            long stackSize = mappedRanges[^1].Length;
            long freeMemory =
                memorySize - programSize - stackSize - mappedRanges.Skip(1).SkipLast(1).Sum(r => r.Length);

            List<Range> freeBlocks = new();
            long largestFree = -1;
            for (int i = 0; i < mappedRanges.Count - 1; i++)
            {
                if (mappedRanges[i].End != mappedRanges[i + 1].Start)
                {
                    Range newRange;
                    try
                    {
                        newRange = new(mappedRanges[i].End, mappedRanges[i + 1].Start);
                    }
                    catch (ArgumentException)
                    {
                        return;
                    }
                    freeBlocks.Add(newRange);
                    if (newRange.Length > largestFree)
                    {
                        largestFree = newRange.Length;
                    }
                }
            }

            totalMemoryBlock.Text = $"Total Memory: {memorySize:N0} bytes";
            totalFreeMemoryBlock.Text = $"Total free Memory: {freeMemory:N0} bytes";

            freeBlocksBlock.Text = $"Number of free blocks: {freeBlocks.Count:N0}";
            largestFreeBlockBlock.Text = $"Largest free contiguous block: {largestFree:N0} bytes " +
                $"({100d - (double)largestFree / freeMemory * 100d:N2}% fragmentation)";

            allocatedBlocksBlock.Text = $"Number of allocated blocks: {mappedRanges.Count - 2:N0}";
            allocatedSizeBlock.Text = $"Total size of allocated blocks: {mappedRanges.Skip(1).SkipLast(1).Sum(m => m.Length):N0} bytes";

            stackSizeBlock.Text = $"Stack size: {stackSize:N0} bytes";
            programSizeBlock.Text = $"Program size: {programSize:N0} bytes";

            memoryGraph.Children.Clear();

            double memoryGraphWidth = heapStatsPanel.ActualWidth - 25;
            if (memoryGraphWidth <= 0)
            {
                return;
            }
            double widthPerByte = memoryGraphWidth / memorySize;
            // Iterate over both mapped and unmapped ranges
            foreach ((Range range, bool mapped) in mappedRanges
                .Select(r => (r, true)).Concat(freeBlocks.Select(b => (b, false))).OrderBy(b => b.Item1.Start))
            {
                double widthWithoutBorder = widthPerByte * range.Length - 1;
                if (widthWithoutBorder < 0)
                {
                    continue;
                }
                memoryGraph.Children.Add(new Rectangle()
                {
                    Fill = mapped ? Brushes.Red : Brushes.LawnGreen,
                    Width = widthWithoutBorder,
                    Height = 32,
                    SnapsToDevicePixels = true,
                    ToolTip = $"{(mapped ? "Mapped" : "Unmapped")}" +
                        $"\nSize: {range.Length:N0} bytes" +
                        $"\nStart: {range.Start:X16}" +
                        $"\nEnd: {range.LastIndex:X16}"
                });
                memoryGraph.Children.Add(new Rectangle()
                {
                    Fill = Brushes.Black,
                    Width = 1,
                    Height = 32,
                    SnapsToDevicePixels = true,
                    ToolTip = $"{(mapped ? "Mapped" : "Unmapped")}" +
                        $"\nSize: {range.Length:N0} bytes" +
                        $"\nStart: {range.Start:X16}" +
                        $"\nEnd: {range.LastIndex:X16}"
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

            int lineCount = (int)(programGrid.ActualHeight / lineHeight);
            for (int i = 0; i < lineCount; i++)
            {
                ContextMenus.ProgramContextMenu contextMenu = new();
                contextMenu.AddressSaved += ContextMenu_AddressSaved;
                contextMenu.LabelAdded += ContextMenu_LabelAddedWithAddress;
                contextMenu.Jumped += ContextMenu_Jumped;
                contextMenu.BreakpointToggled += ContextMenu_BreakpointToggled;
                contextMenu.Edited += ContextMenu_Edited;
                contextMenu.MemoryScrolled += ContextMenu_MemoryScrolled;

                BreakpointButton breakpointButton = new()
                {
                    Height = lineHeight,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    ContextMenu = contextMenu
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
                    ContextMenu = contextMenu
                });
                programLinesPanel.Children.Add(new TextBlock()
                {
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = codeFont,
                    Height = lineHeight,
                    ContextMenu = contextMenu
                });
                programLabelsPanel.Children.Add(new TextBlock()
                {
                    Foreground = highlighter.LabelDefinitionColor,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 5, 0),
                    FontFamily = codeFont,
                    Height = lineHeight,
                    ContextMenu = contextMenu
                });
                programCodePanel.Children.Add(new TextBlock()
                {
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 0, 0),
                    FontFamily = codeFont,
                    Height = lineHeight,
                    ContextMenu = contextMenu
                });
            }

            programScroll.ViewportSize = lineCount;

            UpdateDisassemblyView();
        }

        public void ReloadMemoryView()
        {
            memoryAddressPanel.Children.Clear();
            foreach (StackPanel panel in memoryBytePanels)
            {
                panel.Children.Clear();
            }
            foreach (StackPanel panel in memoryAsciiPanels)
            {
                panel.Children.Clear();
            }

            int lineCount = (int)(memoryDataRow.ActualHeight / lineHeight);
            for (int i = 0; i < lineCount; i++)
            {
                memoryAddressPanel.Children.Add(new TextBlock()
                {
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = codeFont,
                    Height = lineHeight
                });

                foreach (StackPanel panel in memoryBytePanels.Concat(memoryAsciiPanels))
                {
                    ContextMenus.MemoryContextMenu contextMenu = new();
                    contextMenu.AddressSaved += ContextMenu_AddressSaved;
                    contextMenu.LabelAdded += ContextMenu_LabelAddedWithAddress;
                    contextMenu.ProgramScrolled += ContextMenu_ProgramScrolled;

                    TextBlock dataBlock = new()
                    {
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontFamily = codeFont,
                        Height = lineHeight,
                        ContextMenu = contextMenu
                    };
                    dataBlock.MouseDown += MemoryBlock_MouseDown;
                    panel.Children.Add(dataBlock);
                }
            }

            memoryScroll.ViewportSize = lineCount;

            UpdateMemoryView();
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

        private void PromptInstructionPatch(ulong address, bool continuous)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            while (continuous && address < (ulong)DebuggingProcessor.Memory.Length)
            {
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

                PatchDialog dialog = new(currentLine, address, (int)instructionSize + proceedingNops, (int)instructionSize, labels)
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
                    DebuggingProcessor.Memory[(int)address + i] = 0x01; // NOP
                }

                DisassembleFromProgramOffset(address, true);
                UpdateAllInformation();

                address += (ulong)dialog.AssembledBytes.Length;
            }
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

        private void DrawPointerArrow(Register register, ulong targetAddress, int startIndentationIndex)
        {
            if (!currentlyRenderedInstructions.TryGetValue(targetAddress, out int targetIndex))
            {
                return;
            }

            int indentationIndex = currentlyRenderedPointerArrows.GetValueOrDefault(targetAddress);

            double innerX = programJumpArrowCanvas.ActualWidth - 2;
            double outerX = innerX - jumpArrowMinSize -
                ((startIndentationIndex + indentationIndex) * jumpArrowSpacing * 2);
            double targetY = targetIndex * lineHeight + jumpArrowOffset;

            // We only need to draw the arrow for 1 register per address
            if (currentlyRenderedPointerArrows.TryAdd(targetAddress, 0))
            {
                programJumpArrowCanvas.Children.Add(new Line()
                {
                    X1 = outerX,
                    Y1 = targetY,
                    X2 = innerX,
                    Y2 = targetY,
                    StrokeThickness = 1,
                    Stroke = Brushes.LimeGreen,
                    SnapsToDevicePixels = true
                });
                // Arrow line head
                programJumpArrowCanvas.Children.Add(new Line()
                {
                    X1 = innerX,
                    Y1 = targetY + 1,
                    X2 = innerX - jumpArrowHeadSize,
                    Y2 = targetY - jumpArrowHeadSize + 1,
                    StrokeThickness = 1,
                    Stroke = Brushes.LimeGreen,
                    SnapsToDevicePixels = true
                });
                programJumpArrowCanvas.Children.Add(new Line()
                {
                    X1 = innerX,
                    Y1 = targetY + 1,
                    X2 = innerX - jumpArrowHeadSize,
                    Y2 = targetY + jumpArrowHeadSize + 1,
                    StrokeThickness = 1,
                    Stroke = Brushes.LimeGreen,
                    SnapsToDevicePixels = true
                });
            }
            currentlyRenderedPointerArrows[targetAddress]++;

            Label registerNameLabel = new()
            {
                Content = register.ToString(),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontFamily = codeFont,
                FontSize = 10,
                Padding = new Thickness(0),
                Background = Brushes.White,
                Foreground = Brushes.Green,
                Width = jumpArrowSpacing * 2,
                Height = lineHeight,
                SnapsToDevicePixels = true
            };
            Canvas.SetTop(registerNameLabel, targetIndex * lineHeight);
            Canvas.SetRight(registerNameLabel, innerX - outerX);
            programJumpArrowCanvas.Children.Add(registerNameLabel);
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

        private void ProgramGrid_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            programScroll.Value -= Math.CopySign(programScroll.ActualHeight / lineHeight / 4, e.Delta);
            UpdateDisassemblyView();
        }

        private void MemoryGrid_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            memoryScroll.Value -= Math.CopySign(memoryScroll.ActualHeight / lineHeight / 4, e.Delta);
            UpdateMemoryView();
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
                case 2:  // Regions tab
                    UpdateMemoryRegionListView();
                    break;
                case 3:  // Labels tab
                    UpdateLabelListView();
                    break;
                case 4:  // Heap stats tab
                    UpdateHeapStatsView();
                    break;
            }
        }

        private void MemoryTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            switch (mainTabControl.SelectedIndex)
            {
                case 0:  // Memory tab
                    UpdateMemoryView();
                    break;
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
            UpdateDisassemblyView();
        }

        private void DisassemblePartialItem_Click(object sender, RoutedEventArgs e)
        {
            if (DebuggingProcessor is null || (processorRunner?.IsBusy ?? true))
            {
                return;
            }

            DisassembleFromProgramOffset(DebuggingProcessor.Registers[(int)Register.rpo], true);
            UpdateDisassemblyView();
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

        private void ContextMenu_AddressAdded(ContextMenus.IAddressContextMenu sender)
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

        private void ContextMenu_AddressRemoved(ContextMenus.IAddressContextMenu sender)
        {
            _ = savedAddresses.Remove(sender.Address);
            UpdateAllInformation();
        }

        private void ContextMenu_BreakpointToggled(ContextMenus.IAddressContextMenu sender)
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

        private void ContextMenu_Jumped(ContextMenus.IAddressContextMenu sender)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            DebuggingProcessor.Registers[(int)Register.rpo] = sender.Address;

            UpdateAllInformation();
        }

        private void ContextMenu_LabelAddedWithAddress(ContextMenus.IAddressContextMenu sender)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            CreateLabelPromptName(sender.Address);

            UpdateAllInformation();
        }

        private void ContextMenu_AddressSaved(ContextMenus.IAddressContextMenu sender)
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

            PromptInstructionPatch(DebuggingProcessor.Registers[(int)Register.rpo], true);

            UpdateAllInformation();
        }

        private void ContextMenu_Edited(ContextMenus.IAddressContextMenu sender)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            PromptInstructionPatch(sender.Address, true);

            UpdateAllInformation();
        }

        private void programJumpArrowCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateDisassemblyView();
        }

        private void memoryScroll_Scroll(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e)
        {
            UpdateMemoryView();
        }

        private void MemoryGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ReloadMemoryView();
        }

        private void MemoryBlock_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            SelectedMemoryAddress = (ulong)((FrameworkElement)sender).Tag;
            UpdateAllInformation();
        }

        private void ContextMenu_MemoryScrolled(ContextMenus.IAddressContextMenu sender)
        {
            ScrollAndSelectMemoryOffset(sender.Address);
            // Switch to memory view
            memoryTabControl.SelectedIndex = 0;
        }

        private void ContextMenu_ProgramScrolled(ContextMenus.IAddressContextMenu sender)
        {
            ScrollToProgramOffset(sender.Address);
            // Switch to program view
            mainTabControl.SelectedIndex = 0;
        }

        private void heapStatsPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateHeapStatsView();
        }

        private void GoToProgramItem_Click(object sender, RoutedEventArgs e)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            long? address = AskHexadecimalNumber("Enter program offset to scroll to in hexadecimal", "Enter Offset");
            if (address is not null)
            {
                ScrollToProgramOffset((ulong)address);
                // Switch to program view
                mainTabControl.SelectedIndex = 0;
            }
        }

        private void GoToMemoryItem_Click(object sender, RoutedEventArgs e)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            long? address = AskHexadecimalNumber("Enter memory address to scroll to in hexadecimal", "Enter Address");
            if (address is not null)
            {
                ScrollAndSelectMemoryOffset((ulong)address);
                // Switch to memory view
                memoryTabControl.SelectedIndex = 0;
            }
        }

        private void RegisterContextMenu_AddressSaved(ContextMenus.RegisterContextMenu sender)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            SaveAddressPromptName(DebuggingProcessor.Registers[(int)sender.RepresentedRegister]);

            UpdateAllInformation();
        }

        private void RegisterContextMenu_LabelAdded(ContextMenus.RegisterContextMenu sender)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            CreateLabelPromptName(DebuggingProcessor.Registers[(int)sender.RepresentedRegister]);

            UpdateAllInformation();
        }

        private void RegisterContextMenu_ProgramScrolled(ContextMenus.RegisterContextMenu sender)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            ScrollToProgramOffset(DebuggingProcessor.Registers[(int)sender.RepresentedRegister]);
            // Switch to program view
            mainTabControl.SelectedIndex = 0;
        }

        private void RegisterContextMenu_MemoryScrolled(ContextMenus.RegisterContextMenu sender)
        {
            if (DebuggingProcessor is null)
            {
                return;
            }

            ScrollAndSelectMemoryOffset(DebuggingProcessor.Registers[(int)sender.RepresentedRegister]);
            // Switch to memory view
            memoryTabControl.SelectedIndex = 0;
        }
    }
}
