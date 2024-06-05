using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AssEmbly.DebuggerGUI
{
    public class VirtualConsoleOutputStream(TextBlock consoleText, Dispatcher wpfDispatcher) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        private long _length = 0;
        public override long Length => _length;
        public override long Position
        {
            get => Length;
            set => throw new NotSupportedException();
        }

        private readonly List<byte> utf8Buffer = new();

        private readonly UTF8Encoding encoding = new(false, true);

        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(new ReadOnlySpan<byte>(buffer)[offset..(offset + count)]);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            foreach (byte value in buffer)
            {
                WriteByte(value);
            }
        }

        private void _WriteByte(byte value)
        {
            utf8Buffer.Add(value);
            char[] dest = new char[2];
            try
            {
                int written = encoding.GetChars(CollectionsMarshal.AsSpan(utf8Buffer), dest);
                utf8Buffer.Clear();
                consoleText.Text += new string(dest[..written]);
                _length += written;
            }
            catch (DecoderFallbackException) { }
        }

        public override void WriteByte(byte value)
        {
            wpfDispatcher.Invoke(() => _WriteByte(value));
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
    }

    public class VirtualConsoleInputStream(TextBox consoleText, Dispatcher wpfDispatcher) : Stream
    {
        private bool _emptyReadAttempt = false;
        public bool EmptyReadAttempt
        {
            get
            {
                if (_emptyReadAttempt)
                {
                    _emptyReadAttempt = false;
                    return true;
                }
                return false;
            }
            private set => _emptyReadAttempt = value;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => Length;
            set => throw new NotSupportedException();
        }

        private readonly Queue<byte> utf8Buffer = new();

        public override int Read(byte[] buffer, int offset, int count)
        {
            for (int i = offset; i < offset + count; i++)
            {
                int readByte = ReadByte();
                if (readByte == -1)
                {
                    return i - offset;
                }
                buffer[i] = (byte)readByte;
            }
            return count;
        }

        private int _ReadByte()
        {
            if (consoleText.Text.Length == 0)
            {
                EmptyReadAttempt = true;
                return -1;
            }

            if (utf8Buffer.TryDequeue(out byte result))
            {
                return result;
            }

            string inputCharacter = "";
            char readCharacter;
            do
            {
                readCharacter = consoleText.Text[0];
                // Replace \r\n with \n
                if (readCharacter == '\r' && consoleText.Text.Length >= 2 && consoleText.Text[1] == '\n')
                {
                    readCharacter = '\n';
                    consoleText.Text = consoleText.Text[1..];
                }
                inputCharacter += readCharacter;
                consoleText.Text = consoleText.Text[1..];
            } while (char.IsHighSurrogate(readCharacter) && consoleText.Text.Length > 0);

            byte[] utf8Bytes = Encoding.UTF8.GetBytes(inputCharacter);

            // Add remaining UTF-8 bytes to a queue to be retrieved by future reads
            if (utf8Bytes.Length > 1)
            {
                for (int i = 1; i < utf8Bytes.Length; i++)
                {
                    utf8Buffer.Enqueue(utf8Bytes[i]);
                }
            }

            return utf8Bytes[0];
        }

        public override int ReadByte()
        {
            return wpfDispatcher.Invoke(_ReadByte);
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
    }
}
