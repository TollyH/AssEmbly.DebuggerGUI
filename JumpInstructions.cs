namespace AssEmbly.DebuggerGUI
{
    public static class JumpInstructions
    {
        public static readonly HashSet<Opcode> UnconditionalJumps = new()
        {
            new Opcode(0x00, 0x02), new Opcode(0x00, 0x03)
        };

        public static readonly Dictionary<Opcode, (StatusFlags Flags, StatusFlags FlagMask)[]> ConditionalJumps = new()
        {
            // JEQ/JZO
            { new Opcode(0x00, 0x04), new[] { (StatusFlags.Zero, StatusFlags.Zero) } },
            // JNE/JNZ
            { new Opcode(0x00, 0x06), new[] { ((StatusFlags)0, StatusFlags.Zero) } },
            // JLT/JCA
            { new Opcode(0x00, 0x08), new[] { (StatusFlags.Carry, StatusFlags.Carry) } },
            // JLE
            { new Opcode(0x00, 0x0A), new[] { (StatusFlags.Carry, StatusFlags.Carry), (StatusFlags.Zero, StatusFlags.Zero) } },
            // JGT
            { new Opcode(0x00, 0x0C), new[] { ((StatusFlags)0, StatusFlags.ZeroAndCarry) } },
            // JGE
            { new Opcode(0x00, 0x0E), new[] { ((StatusFlags)0, StatusFlags.Carry) } },

            // SIGN_JLT
            { new Opcode(0x01, 0x00), new[] { (StatusFlags.Sign, StatusFlags.SignAndOverflow), (StatusFlags.Overflow, StatusFlags.SignAndOverflow) } },
            // SIGN_JLE
            { new Opcode(0x01, 0x02), new[] { (StatusFlags.Sign, StatusFlags.SignAndOverflow), (StatusFlags.Overflow, StatusFlags.SignAndOverflow), (StatusFlags.Zero, StatusFlags.Zero) } },
            // SIGN_JGT
            { new Opcode(0x01, 0x04), new[] { (StatusFlags.SignAndOverflow, StatusFlags.SignAndOverflow | StatusFlags.Zero), ((StatusFlags)0, StatusFlags.SignAndOverflow | StatusFlags.Zero) } },
            // SIGN_JGE
            { new Opcode(0x01, 0x06), new[] { (StatusFlags.SignAndOverflow, StatusFlags.SignAndOverflow), ((StatusFlags)0, StatusFlags.SignAndOverflow) } },
            // SIGN_JSI
            { new Opcode(0x01, 0x08), new[] { (StatusFlags.Sign, StatusFlags.Sign) } },
            // SIGN_JNS
            { new Opcode(0x01, 0x0A), new[] { ((StatusFlags)0, StatusFlags.Sign) } },
            // SIGN_JOV
            { new Opcode(0x01, 0x0C), new[] { (StatusFlags.Overflow, StatusFlags.Overflow) } },
            // SIGN_JNO
            { new Opcode(0x01, 0x0E), new[] { ((StatusFlags)0, StatusFlags.Overflow) } },
        };
    }
}
