using System;
using System.Text;

namespace Oxfall.Core.Net
{
    /// <summary>A message that does not follow the protocol: too short, a bad length or an unknown type.</summary>
    public sealed class ProtocolException : Exception
    {
        public ProtocolException(string message) : base(message) { }
    }

    /// <summary>
    /// Writes a network message into a reusable byte buffer, little-endian. The same writer is
    /// reused message after message, so sending allocates nothing but the final copy (if any).
    /// </summary>
    public sealed class NetWriter
    {
        private byte[] _buffer;

        public NetWriter(int capacity = 256) => _buffer = new byte[Math.Max(16, capacity)];

        public byte[] Buffer => _buffer;
        public int Length { get; private set; }

        public void Reset() => Length = 0;

        public byte[] ToArray()
        {
            var copy = new byte[Length];
            Array.Copy(_buffer, copy, Length);
            return copy;
        }

        public void Byte(byte value)
        {
            Ensure(1);
            _buffer[Length++] = value;
        }

        public void Bool(bool value) => Byte(value ? (byte)1 : (byte)0);

        public void UShort(ushort value)
        {
            Ensure(2);
            _buffer[Length++] = (byte)value;
            _buffer[Length++] = (byte)(value >> 8);
        }

        public void Int(int value)
        {
            Ensure(4);
            _buffer[Length++] = (byte)value;
            _buffer[Length++] = (byte)(value >> 8);
            _buffer[Length++] = (byte)(value >> 16);
            _buffer[Length++] = (byte)(value >> 24);
        }

        public void Float(float value) => Int(BitConverter.SingleToInt32Bits(value));

        /// <summary>UTF-8 with a 2-byte length; longer strings are cut to <paramref name="maxBytes"/>.</summary>
        public void String(string value, int maxBytes = 256)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? "");
            int n = Math.Min(bytes.Length, maxBytes);
            UShort((ushort)n);
            Ensure(n);
            Array.Copy(bytes, 0, _buffer, Length, n);
            Length += n;
        }

        private void Ensure(int more)
        {
            if (Length + more <= _buffer.Length) return;
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, Length + more));
        }
    }

    /// <summary>Reads a message written by <see cref="NetWriter"/>. Reading past the end throws.</summary>
    public sealed class NetReader
    {
        private readonly byte[] _data;
        private readonly int _end;
        private int _position;

        public NetReader(byte[] data) : this(data, 0, data?.Length ?? 0) { }

        public NetReader(byte[] data, int offset, int count)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || offset + count > data.Length) throw new ArgumentOutOfRangeException(nameof(count));
            _position = offset;
            _end = offset + count;
        }

        public int Remaining => _end - _position;

        public byte Byte()
        {
            Need(1);
            return _data[_position++];
        }

        public bool Bool() => Byte() != 0;

        public ushort UShort()
        {
            Need(2);
            int v = _data[_position] | (_data[_position + 1] << 8);
            _position += 2;
            return (ushort)v;
        }

        public int Int()
        {
            Need(4);
            int v = _data[_position] | (_data[_position + 1] << 8) | (_data[_position + 2] << 16) | (_data[_position + 3] << 24);
            _position += 4;
            return v;
        }

        public float Float() => BitConverter.Int32BitsToSingle(Int());

        public string String()
        {
            int n = UShort();
            Need(n);
            var s = Encoding.UTF8.GetString(_data, _position, n);
            _position += n;
            return s;
        }

        private void Need(int n)
        {
            if (_position + n > _end) throw new ProtocolException("Message ended early.");
        }
    }
}
