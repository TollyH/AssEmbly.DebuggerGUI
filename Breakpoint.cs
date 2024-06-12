using System.Buffers.Binary;

namespace AssEmbly.DebuggerGUI
{
    public enum BreakpointType
    {
        RegisterValue,
        RegisterChanged,
        MemoryValue,
        MemoryChanged
    }

    public interface IBreakpoint : IEquatable<IBreakpoint>
    {
        public bool ShouldBreak(Processor checkProcessor);
    }

    public interface IRegisterBreakpoint : IBreakpoint
    {
        public Register CheckRegister { get; }
    }

    public interface IMemoryBreakpoint : IBreakpoint
    {
        public ulong CheckAddress { get; }
        public PointerReadSize CheckSize { get; }
    }

    public class RegisterValueBreakpoint(Register checkRegister, ulong targetValue) : IRegisterBreakpoint
    {
        public Register CheckRegister { get; } = checkRegister;
        public ulong TargetValue { get; } = targetValue;

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

    public class RegisterChangedBreakpoint(Register checkRegister, ulong targetValue) : IRegisterBreakpoint
    {
        public Register CheckRegister { get; } = checkRegister;
        public ulong InitialValue { get; private set; } = targetValue;

        public bool ShouldBreak(Processor checkProcessor)
        {
            ulong value = checkProcessor.Registers[(int)CheckRegister];
            if (value != InitialValue)
            {
                InitialValue = value;
                return true;
            }
            return false;
        }

        public bool Equals(RegisterChangedBreakpoint other)
        {
            return CheckRegister == other.CheckRegister && InitialValue == other.InitialValue;
        }

        public bool Equals(IBreakpoint? other)
        {
            return other is RegisterChangedBreakpoint otherBreakpoint && Equals(otherBreakpoint);
        }

        public override bool Equals(object? obj)
        {
            return obj is RegisterChangedBreakpoint other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine((int)BreakpointType.RegisterChanged, (int)CheckRegister);
        }

        public static bool operator ==(RegisterChangedBreakpoint left, RegisterChangedBreakpoint right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(RegisterChangedBreakpoint left, RegisterChangedBreakpoint right)
        {
            return !left.Equals(right);
        }
    }

    public class MemoryValueBreakpoint(ulong checkAddress, PointerReadSize checkSize, ulong targetValue) : IMemoryBreakpoint
    {
        public ulong CheckAddress { get; } = checkAddress;
        public PointerReadSize CheckSize { get; } = checkSize;
        public ulong TargetValue { get; } = targetValue;

        public bool ShouldBreak(Processor checkProcessor)
        {
            ulong value = CheckSize switch
            {
                PointerReadSize.Byte => checkProcessor.Memory[CheckAddress],
                PointerReadSize.Word => BinaryPrimitives.ReadUInt16LittleEndian(checkProcessor.Memory.AsSpan((int)CheckAddress)),
                PointerReadSize.DoubleWord => BinaryPrimitives.ReadUInt32LittleEndian(checkProcessor.Memory.AsSpan((int)CheckAddress)),
                PointerReadSize.QuadWord => BinaryPrimitives.ReadUInt64LittleEndian(checkProcessor.Memory.AsSpan((int)CheckAddress)),
                _ => throw new ArgumentException("Invalid check size")
            };
            return value == TargetValue;
        }

        public bool Equals(MemoryValueBreakpoint other)
        {
            return CheckAddress == other.CheckAddress && CheckSize == other.CheckSize && TargetValue == other.TargetValue;
        }

        public bool Equals(IBreakpoint? other)
        {
            return other is MemoryValueBreakpoint otherBreakpoint && Equals(otherBreakpoint);
        }

        public override bool Equals(object? obj)
        {
            return obj is MemoryValueBreakpoint other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine((int)BreakpointType.MemoryValue, CheckAddress, (int)CheckSize, TargetValue);
        }

        public static bool operator ==(MemoryValueBreakpoint left, MemoryValueBreakpoint right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(MemoryValueBreakpoint left, MemoryValueBreakpoint right)
        {
            return !left.Equals(right);
        }
    }

    public class MemoryChangedBreakpoint(ulong checkAddress, PointerReadSize checkSize, ulong initialValue) : IMemoryBreakpoint
    {
        public ulong CheckAddress { get; } = checkAddress;
        public PointerReadSize CheckSize { get; } = checkSize;
        public ulong InitialValue { get; set; } = initialValue;

        public bool ShouldBreak(Processor checkProcessor)
        {
            ulong value = CheckSize switch
            {
                PointerReadSize.Byte => checkProcessor.Memory[CheckAddress],
                PointerReadSize.Word => BinaryPrimitives.ReadUInt16LittleEndian(checkProcessor.Memory.AsSpan((int)CheckAddress)),
                PointerReadSize.DoubleWord => BinaryPrimitives.ReadUInt32LittleEndian(checkProcessor.Memory.AsSpan((int)CheckAddress)),
                PointerReadSize.QuadWord => BinaryPrimitives.ReadUInt64LittleEndian(checkProcessor.Memory.AsSpan((int)CheckAddress)),
                _ => throw new ArgumentException("Invalid check size")
            };
            if (value != InitialValue)
            {
                InitialValue = value;
                return true;
            }
            return false;
        }

        public bool Equals(MemoryChangedBreakpoint other)
        {
            return CheckAddress == other.CheckAddress && CheckSize == other.CheckSize && InitialValue == other.InitialValue;
        }

        public bool Equals(IBreakpoint? other)
        {
            return other is MemoryChangedBreakpoint otherBreakpoint && Equals(otherBreakpoint);
        }

        public override bool Equals(object? obj)
        {
            return obj is MemoryChangedBreakpoint other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine((int)BreakpointType.MemoryChanged, CheckAddress, (int)CheckSize);
        }

        public static bool operator ==(MemoryChangedBreakpoint left, MemoryChangedBreakpoint right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(MemoryChangedBreakpoint left, MemoryChangedBreakpoint right)
        {
            return !left.Equals(right);
        }
    }
}
