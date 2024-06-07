using System.Buffers.Binary;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AssEmbly.DebuggerGUI
{
    /// <summary>
    /// Interaction logic for PatchDialog.xaml
    /// </summary>
    public partial class PatchDialog : Window
    {
        public enum ResultType
        {
            Success,
            NopOverwrite,
            Fail
        }

        public byte[] AssembledBytes { get; private set; } = Array.Empty<byte>();

        private ResultType _assemblyResult;
        public ResultType AssemblyResult
        {
            get => _assemblyResult;
            private set
            {
                _assemblyResult = value;
                instructionStatus.Foreground = _assemblyResult switch
                {
                    ResultType.Success => Brushes.LawnGreen,
                    ResultType.NopOverwrite => Brushes.Orange,
                    ResultType.Fail => Brushes.Red,
                    _ => Brushes.White
                };
            }
        }

        public int MaxBytes { get; }
        public int TargetBytes { get; }

        public Dictionary<string, ulong> Labels { get; }

        public PatchDialog(string currentLine, int maxBytes, int targetBytes, Dictionary<string, ulong> labels)
        {
            InitializeComponent();

            MaxBytes = maxBytes;
            TargetBytes = targetBytes;

            if (maxBytes == targetBytes)
            {
                messageBlock.Text += $"\nInstruction must be less than {maxBytes} bytes.";
            }
            else
            {
                messageBlock.Text += $"\nInstruction must be less than {maxBytes} bytes," +
                    $" and should be less than {targetBytes} bytes to not replace existing NOP instructions.";
            }

            Labels = labels;

            inputBox.Text = currentLine;
            inputBox.Focus();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void inputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                DialogResult = true;
                Close();
            }
        }

        private void inputBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            try
            {
                string[] lineComponents = Assembler.ParseLine(inputBox.Text);
                if (lineComponents.Length == 0)
                {
                    throw new Exception("Instruction cannot be empty.");
                }

                (AssembledBytes, List<(string LabelName, ulong AddressOffset)> labelReferences) =
                    Assembler.AssembleStatement(lineComponents[0], lineComponents[1..]);

                foreach ((string name, ulong offset) in labelReferences)
                {
                    if (!Labels.TryGetValue(name, out ulong address))
                    {
                        throw new Exception($"A label with name \"{name}\" is not defined.");
                    }
                    BinaryPrimitives.WriteUInt64LittleEndian(AssembledBytes.AsSpan((int)offset), address);
                }

                if (AssembledBytes.Length > MaxBytes)
                {
                    throw new Exception($"Instruction is too large ({AssembledBytes.Length} > {MaxBytes} bytes).");
                }

                if (AssembledBytes.Length > TargetBytes)
                {
                    AssemblyResult = ResultType.NopOverwrite;
                    instructionStatus.Text = "Instruction is larger than existing instruction and will overwrite " +
                        $"{AssembledBytes.Length - TargetBytes} existing NOP statements.";
                }
                else if (AssembledBytes.Length < TargetBytes)
                {
                    AssemblyResult = ResultType.Success;
                    instructionStatus.Text = $"Instruction is smaller than existing instruction ({AssembledBytes.Length} < {TargetBytes})" +
                        $" and will insert {TargetBytes - AssembledBytes.Length} new NOP statements to fill empty space.";
                }
                else
                {
                    AssemblyResult = ResultType.Success;
                    instructionStatus.Text = "Instruction is the same size as existing instruction.";
                }
            }
            catch (Exception exc)
            {
                AssemblyResult = ResultType.Fail;
                instructionStatus.Text = exc.Message;
            }
        }
    }
}
