using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Media;

namespace AssEmbly.DebuggerGUI
{
    public partial class SyntaxHighlighting
    {
        // Colors - Based on VSCode "Dark Modern" theme
        public SolidColorBrush DefaultColor { get; set; } = new(new Color() { R = 0xD4, G = 0xD4, B = 0xD4, A = 0xFF });

        // Keywords
        public SolidColorBrush MnemonicColor { get; set; } = new(new Color() { R = 0xC5, G = 0x86, B = 0xC0, A = 0xFF });
        public SolidColorBrush DirectiveColor { get; set; } = new(new Color() { R = 0x56, G = 0x9C, B = 0xD6, A = 0xFF });

        // Literals
        public SolidColorBrush NumericLiteralColor { get; set; } = new(new Color() { R = 0xB5, G = 0xCE, B = 0xA8, A = 0xFF });

        // Labels/addresses
        public SolidColorBrush LabelDefinitionColor { get; set; } = new(new Color() { R = 0x56, G = 0x9C, B = 0xD6, A = 0xFF });
        public SolidColorBrush LabelReferenceColor { get; set; } = new(new Color() { R = 0x9C, G = 0xDC, B = 0xFE, A = 0xFF });
        public SolidColorBrush LabelLiteralColor { get; set; } = new(new Color() { R = 0x4E, G = 0xC9, B = 0xB0, A = 0xFF });

        // Variables
        public SolidColorBrush AssemblerVariableColor { get; set; } = new(new Color() { R = 0x9C, G = 0xDC, B = 0xFE, A = 0xFF });
        public SolidColorBrush AssemblerConstantColor { get; set; } = new(new Color() { R = 0x56, G = 0x9C, B = 0xD6, A = 0xFF });
        public SolidColorBrush PreDefinedMacroColor { get; set; } = new(new Color() { R = 0x56, G = 0x9C, B = 0xD6, A = 0xFF });

        // Registers
        public SolidColorBrush PointerColor { get; set; } = new(new Color() { R = 0x4E, G = 0xC9, B = 0xB0, A = 0xFF });
        public SolidColorBrush RegisterColor { get; set; } = new(new Color() { R = 0xDC, G = 0xDC, B = 0xAA, A = 0xFF });

        // Parameters
        public SolidColorBrush MacroParameterColor { get; set; } = new(new Color() { R = 0x9C, G = 0xDC, B = 0xFE, A = 0xFF });

        // Special
        public SolidColorBrush SpecialOperandColor { get; set; } = new(new Color() { R = 0xDC, G = 0xDC, B = 0xAA, A = 0xFF });
        public SolidColorBrush AnalyzerStateColor { get; set; } = new(new Color() { R = 0xB5, G = 0xCE, B = 0xA8, A = 0xFF });

        // Strings
        public SolidColorBrush QuotedLiteralColor { get; set; } = new(new Color() { R = 0xCE, G = 0x91, B = 0x78, A = 0xFF });

        // Escape sequences
        public SolidColorBrush EscapeSequenceColor { get; set; } = new(new Color() { R = 0xD7, G = 0xBA, B = 0x7D, A = 0xFF });

        // Comments
        public SolidColorBrush CommentColor { get; set; } = new(new Color() { R = 0x6A, G = 0x99, B = 0x55, A = 0xFF });

        public Inline[] HighlightLine(string line)
        {
            Dictionary<Regex, SolidColorBrush> components = GetComponents();
            Dictionary<Regex, SolidColorBrush> stringComponents = GetStringComponents();
            List<Inline> inlines = new();

            char? openQuote = null;
            // Placeholder value - will be updated to the corresponding closing character if a quoted literal is opened
            char closeQuote = '\0';

            for (int i = 0; i < line.Length;)
            {
                char c = line[i];

                if (openQuote is not null)
                {
                    // Currently inside a quoted literal
                    Match match = EscapeSequenceRegex().Match(line, i);
                    if (match.Success && match.Index == i)
                    {
                        inlines.Add(new Run(match.Value)
                        {
                            Foreground = EscapeSequenceColor
                        });
                        i += match.Length;
                        // Escape sequences prevent closure of quotes
                        continue;
                    }

                    if (!TryMatchComponents(stringComponents, inlines, line, ref i))
                    {
                        // No patterns matched - use default colour
                        inlines.Add(new Run(c.ToString())
                        {
                            Foreground = QuotedLiteralColor
                        });
                        if (c == closeQuote)
                        {
                            openQuote = null;
                        }
                        i++;
                    }

                    // Skip all other highlight checks inside quoted literals
                    continue;
                }

                if (quotes.TryGetValue(c, out closeQuote))
                {
                    // Open a quoted literal
                    openQuote = c;
                    inlines.Add(new Run(c.ToString())
                    {
                        Foreground = QuotedLiteralColor
                    });
                    i++;
                    continue;
                }

                if (!TryMatchComponents(components, inlines, line, ref i))
                {
                    // No patterns matched - use default colour
                    inlines.Add(new Run(line[i++].ToString())
                    {
                        Foreground = DefaultColor
                    });
                }
            }

            return inlines.ToArray();
        }

        // Patterns that match everywhere except inside quoted literals
        private Dictionary<Regex, SolidColorBrush> GetComponents()
        {
            return new Dictionary<Regex, SolidColorBrush>()
            {
                { MnemonicRegex(), MnemonicColor },
                { DirectiveRegex(), DirectiveColor },

                { NumericLiteralBinaryRegex(), NumericLiteralColor },
                { NumericLiteralDecimalRegex(), NumericLiteralColor },
                { NumericLiteralHexadecimalRegex(), NumericLiteralColor },
                { AddressLiteralDecimalRegex(), NumericLiteralColor },
                { AddressLiteralHexadecimalRegex(), NumericLiteralColor },
                { AddressLiteralBinaryRegex(), NumericLiteralColor },

                { LabelDefinitionRegex(), LabelDefinitionColor },
                { LabelLiteralRegex(), LabelLiteralColor },
                { LabelReferenceRegex(), LabelReferenceColor },

                { AssemblerVariableRegex(), AssemblerVariableColor },
                { AssemblerConstantRegex(), AssemblerConstantColor },
                { PreDefinedMacroRegex(), PreDefinedMacroColor },

                { PointerRegex(), PointerColor },
                { RegisterRegex(), RegisterColor },

                { MacroParameterRegex(), MacroParameterColor },

                { ConditionOperandRegex(), SpecialOperandColor },
                { OperationOperandRegex(), SpecialOperandColor },
                { SeverityOperandRegex(), SpecialOperandColor },
                { AnalyzerStateRegex(), AnalyzerStateColor },

                { CommentRegex(), CommentColor },
            };
        }

        // Patterns that match inside quoted literals, but cannot stop the string from closing
        // (i.e. does not include escape sequences).
        private Dictionary<Regex, SolidColorBrush> GetStringComponents()
        {
            return new Dictionary<Regex, SolidColorBrush>()
            {
                { AssemblerVariableRegex(), AssemblerVariableColor },
                { AssemblerConstantRegex(), AssemblerConstantColor },
                { PreDefinedMacroRegex(), PreDefinedMacroColor },
            };
        }

        private static bool TryMatchComponents(Dictionary<Regex, SolidColorBrush> components,
            List<Inline> inlines, string line, ref int i)
        {
            bool anyMatch = false;
            foreach ((Regex regex, SolidColorBrush color) in components)
            {
                Match match = regex.Match(line, i);
                if (match.Success && match.Index == i)
                {
                    inlines.Add(new Run(match.Value)
                    {
                        Foreground = color
                    });
                    i += match.Length;
                    // This highlight has captured this text region - don't check anymore patterns
                    anyMatch = true;
                    break;
                }
            }
            return anyMatch;
        }

        // RegEx matches
        // Keywords
        [GeneratedRegex("(?i)^[ \t]*!?[ \t]*(?:HLT|NOP|JMP|JEQ|JZO|JNE|JNZ|JLT|JCA|JLE|JGT|JGE|JNC|ADD|ICR|SUB|DCR|MUL|DIV|DVR|REM|SHL|SHR|AND|ORR|XOR|NOT|RNG|TST|CMP|MVB|MVW|MVD|MVQ|PSH|POP|CAL|RET|WCN|WCB|WCX|WCC|WFN|WFB|WFX|WFC|OFL|CFL|DFL|FEX|FSZ|RCC|RFC|SIGN_JLT|SIGN_JLE|SIGN_JGT|SIGN_JGE|SIGN_JSI|SIGN_JNS|SIGN_JOV|SIGN_JNO|SIGN_DIV|SIGN_DVR|SIGN_REM|SIGN_SHR|SIGN_MVB|SIGN_MVW|SIGN_MVD|SIGN_WCN|SIGN_WCB|SIGN_WFN|SIGN_WFB|SIGN_EXB|SIGN_EXW|SIGN_EXD|SIGN_NEG|FLPT_ADD|FLPT_SUB|FLPT_MUL|FLPT_DIV|FLPT_DVR|FLPT_REM|FLPT_SIN|FLPT_ASN|FLPT_COS|FLPT_ACS|FLPT_TAN|FLPT_ATN|FLPT_PTN|FLPT_POW|FLPT_LOG|FLPT_WCN|FLPT_WFN|FLPT_EXH|FLPT_EXS|FLPT_SHS|FLPT_SHH|FLPT_NEG|FLPT_UTF|FLPT_STF|FLPT_FTS|FLPT_FCS|FLPT_FFS|FLPT_FNS|FLPT_CMP|EXTD_BSW|ASMX_LDA|ASMX_LDF|ASMX_CLA|ASMX_CLF|ASMX_AEX|ASMX_FEX|ASMX_CAL|HEAP_ALC|HEAP_TRY|HEAP_REA|HEAP_TRE|HEAP_FRE|EXTD_QPF|EXTD_QPV|EXTD_CSS|EXTD_HLT|EXTD_MPA|FSYS_CWD|FSYS_GWD|FSYS_CDR|FSYS_DDR|FSYS_DDE|FSYS_DEX|FSYS_CPY|FSYS_MOV|FSYS_BDL|FSYS_GNF|FSYS_GND|FSYS_GCT|FSYS_GMT|FSYS_GAT|FSYS_SCT|FSYS_SMT|FSYS_SAT|TERM_CLS|TERM_AEE|TERM_AED|TERM_SCY|TERM_SCX|TERM_GCY|TERM_GCX|TERM_GSY|TERM_GSX|TERM_BEP|TERM_SFC|TERM_SBC|TERM_RSC|EXTD_SLP)(?= |;|$)")]
        private static partial Regex MnemonicRegex();
        [GeneratedRegex("(?i)^[ \t]*!?[ \t]*%(?:PAD|DAT|NUM|IMP|MACRO|ENDMACRO|DELMACRO|ANALYZER|MESSAGE|IBF|DEBUG|LABEL_OVERRIDE|STOP|REPEAT|ENDREPEAT|ASM_ONCE|DEFINE|UNDEFINE|VAROP|IF|ELSE|ELSE_IF|ENDIF|WHILE|ENDWHILE)(?= |;|$)")]
        private static partial Regex DirectiveRegex();

        // Literals
        [GeneratedRegex(@"(?i)(?<=\s|,|\(|\[)-?0b[0-1_]+?(?=\s|,|$|;|\)|\])")]
        private static partial Regex NumericLiteralBinaryRegex();
        [GeneratedRegex(@"(?<=\s|,|\(|\[)[\-0-9._][0-9_.]*?(?=\s|,|$|;|\)|\])")]
        private static partial Regex NumericLiteralDecimalRegex();
        [GeneratedRegex(@"(?i)(?<=\s|,|\(|\[)-?0x[0-9a-f_]+?(?=\s|,|$|;|\)|\])")]
        private static partial Regex NumericLiteralHexadecimalRegex();
        [GeneratedRegex(@"(?<=\s|,|\(|\[):[0-9_]+?(?=\s|,|$|;|\)|\])")]
        private static partial Regex AddressLiteralDecimalRegex();
        [GeneratedRegex(@"(?i)(?<=\s|,|\(|\[):0x[0-9a-f_]+?(?=\s|,|$|;|\)|\])")]
        private static partial Regex AddressLiteralHexadecimalRegex();
        [GeneratedRegex(@"(?i)(?<=\s|,|\(|\[):0b[0-1_]+?(?=\s|,|$|;|\)|\])")]
        private static partial Regex AddressLiteralBinaryRegex();

        // Labels/addresses
        [GeneratedRegex("^[ \t]*!?[ \t]*:[A-Za-z_][A-Za-z0-9_]*(?:;|$|\\s)")]
        private static partial Regex LabelDefinitionRegex();
        [GeneratedRegex(@"(?<!^)(?<=\s|,|\(|\[|\+):&[A-Za-z_][A-Za-z0-9_]*(?=\s|,|$|;|\)|\[|\])")]
        private static partial Regex LabelLiteralRegex();
        [GeneratedRegex(@"(?<!^)(?<=\s|,|\():[A-Za-z_][A-Za-z0-9_]*?(?=\s|,|$|;|\)|\[)")]
        private static partial Regex LabelReferenceRegex();

        // Variables
        [GeneratedRegex(@"(?<!\\)@[A-Za-z0-9_]+")]
        private static partial Regex AssemblerVariableRegex();
        [GeneratedRegex(@"(?<!\\)@![A-Za-z0-9_]+")]
        private static partial Regex AssemblerConstantRegex();
        [GeneratedRegex("#(?:FILE_PATH|FILE_NAME|FOLDER_PATH)")]
        private static partial Regex PreDefinedMacroRegex();

        // Registers
        [GeneratedRegex(@"(?i)(?<!^)(?<=\s|,|\()[QqDdWwBb]?\*(?:rpo|rso|rsb|rsf|rrv|rfp|rg0|rg1|rg2|rg3|rg4|rg5|rg6|rg7|rg8|rg9)(?=\s|,|$|;|\)|\[)")]
        private static partial Regex PointerRegex();
        [GeneratedRegex(@"(?i)(?<!^)(?<=\s|,|\(|\[-?)(?:rpo|rso|rsb|rsf|rrv|rfp|rg0|rg1|rg2|rg3|rg4|rg5|rg6|rg7|rg8|rg9)(?=\s|,|$|;|\)|\])")]
        private static partial Regex RegisterRegex();

        // Parameters
        [GeneratedRegex(@"(?i)\$[0-9]+!?")]
        private static partial Regex MacroParameterRegex();

        // Special
        [GeneratedRegex(@"(?i)(?<=\s)(?:DEF|NDEF|EQ|NEQ|GT|GTE|LT|LTE)(?=\s|,|$|;)")]
        private static partial Regex ConditionOperandRegex();
        [GeneratedRegex(@"(?i)(?<=\s)(?:ADD|SUB|MUL|DIV|REM|BIT_AND|BIT_OR|BIT_XOR|BIT_NOT|AND|OR|XOR|NOT|SHL|SHR|CMP_EQ|CMP_NEQ|CMP_GT|CMP_GTE|CMP_LT|CMP_LTE)(?=\s|,|$|;)")]
        private static partial Regex OperationOperandRegex();
        [GeneratedRegex(@"(?i)(?<=\s)(?:error|warning|suggestion)(?=\s|,|$|;)")]
        private static partial Regex SeverityOperandRegex();
        [GeneratedRegex(@"(?i)(?<=\s|,)(?:0|1|r)(?=\s|,|$|;)")]
        private static partial Regex AnalyzerStateRegex();

        // Comments
        [GeneratedRegex(";.*")]
        private static partial Regex CommentRegex();

        // Escape sequences (only recognised inside quotes - will prevent quotes being closed if the closing quote is part of the match)
        [GeneratedRegex("\\\\(?:\"|'|\\\\|@|0|a|b|f|n|r|t|v|u[0-9A-Fa-f]{4}|U[0-9A-Fa-f]{8})")]
        private static partial Regex EscapeSequenceRegex();

        // { open quote, close quote }
        private static readonly Dictionary<char, char> quotes = new()
        {
            { '"', '"' },
            { '\'', '\'' }
        };
    }
}
