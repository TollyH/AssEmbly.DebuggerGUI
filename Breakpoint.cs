namespace AssEmbly.DebuggerGUI
{
    public enum BreakpointType
    {
        RegisterValue,
    }

    public interface IBreakpoint : IEquatable<IBreakpoint>
    {
        public bool ShouldBreak(Processor checkProcessor);
    }

    public readonly struct RegisterValueBreakpoint(Register checkRegister, ulong targetValue) : IBreakpoint
    {
        public readonly Register CheckRegister = checkRegister;
        public readonly ulong TargetValue = targetValue;

        public bool ShouldBreak(Processor checkProcessor)
        {
            return checkProcessor.Registers[(int)CheckRegister] == TargetValue;
        }

        public bool Equals(RegisterValueBreakpoint other)
        {
            return CheckRegister == other.CheckRegister && TargetValue == other.TargetValue;
        }

        public bool Equals(IBreakpoint? other)
        {
            return other is RegisterValueBreakpoint otherBreakpoint && Equals(otherBreakpoint);
        }

        public override bool Equals(object? obj)
        {
            return obj is RegisterValueBreakpoint other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine((int)BreakpointType.RegisterValue, (int)CheckRegister, TargetValue);
        }

        public static bool operator ==(RegisterValueBreakpoint left, RegisterValueBreakpoint right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(RegisterValueBreakpoint left, RegisterValueBreakpoint right)
        {
            return !left.Equals(right);
        }
    }
}
