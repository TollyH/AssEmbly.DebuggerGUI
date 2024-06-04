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

        private CancellationTokenSource cancellationTokenSource = new();

        private BackgroundRunner? processorRunner;

        private readonly TextBlock[] registerValueBlocks;
        private readonly TextBlock[] registerValueExtraBlocks;
        private readonly Dictionary<StatusFlags, TextBlock> statusFlagBlocks;

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
                executable = new(File.ReadAllBytes(path));
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

            try
            {
                DebuggingProcessor = new Processor(memorySize, executable.EntryPoint,
                    forceV1.IsChecked ?? false, mapStack.IsChecked ?? true, autoEcho.IsChecked ?? false);
                DebuggingProcessor.LoadProgram(executable.Program);
                processorRunner = new BackgroundRunner(DebuggingProcessor, Dispatcher);
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

            executablePathText.Text = path;
            UpdateAllInformation();
        }

        public void UnloadExecutable()
        {
            cancellationTokenSource.Cancel();
            cancellationTokenSource = new CancellationTokenSource();

            DebuggingProcessor = null;
            processorRunner = null;
            UpdateRunningState(RunningState.Stopped);
        }

        public void UpdateAllInformation()
        {
            UpdateRegistersView();
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
                    blockExtra.Text = $"'{(char)value}'";
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

        private void OnBreak(bool halt)
        {
            UpdateRunningState(RunningState.Paused);
            UpdateAllInformation();
            if (halt)
            {
                UnloadExecutable();
            }
        }

        private void OnException(Exception exception)
        {
            DialogPopup popup = new($"{exception.GetType()}: {exception.Message}",
                "AssEmbly Exception", DialogPopup.ErrorIcon)
            {
                Owner = this
            };
            popup.ShowDialog();
            UnloadExecutable();
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
    }
}
