using System.Text;

namespace IW4.Formats.D3dbsp;

/// <summary>A native entity token and its exact byte range in the input.</summary>
public readonly record struct EntityToken(string Text, int Begin, int End, bool IsQuoted);

/// <summary>Reads entity tokens without normalizing their source byte ranges.</summary>
public ref struct EntityTokenReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public EntityTokenReader(ReadOnlySpan<byte> data) => _data = data;

    public int Position => _position;

    public EntityToken? ReadToken()
    {
        SkipTrivia();
        if (_position >= _data.Length)
            return null;

        int begin = _position;
        byte first = _data[_position++];
        if (first is (byte)'{' or (byte)'}')
            return new EntityToken(((char)first).ToString(), begin, _position, false);
        if (first == (byte)'"')
        {
            string quoted = ReadQuotedToken();
            return new EntityToken(quoted, begin, _position, true);
        }

        while (_position < _data.Length &&
               _data[_position] > 32 &&
               _data[_position] is not (byte)'{' and not (byte)'}')
            _position++;

        return new EntityToken(Encoding.Latin1.GetString(_data[begin.._position]),
            begin, _position, false);
    }

    private string ReadQuotedToken()
    {
        var token = new StringBuilder();
        while (_position < _data.Length)
        {
            byte value = _data[_position++];
            if (value == (byte)'"')
                return token.ToString();
            if (value == (byte)'\\' &&
                _position < _data.Length &&
                _data[_position] is (byte)'"' or (byte)'\\')
                value = _data[_position++];

            token.Append((char)value);
        }

        throw new InvalidDataException("The entity lump ended inside a quoted token.");
    }

    private void SkipTrivia()
    {
        while (true)
        {
            while (_position < _data.Length && _data[_position] <= 32)
                _position++;
            if (_position + 1 >= _data.Length || _data[_position] != (byte)'/')
                return;

            if (_data[_position + 1] == (byte)'/')
            {
                _position += 2;
                while (_position < _data.Length && _data[_position] != (byte)'\n')
                    _position++;
                continue;
            }
            if (_data[_position + 1] != (byte)'*')
                return;

            _position += 2;
            while (_position + 1 < _data.Length &&
                   (_data[_position] != (byte)'*' || _data[_position + 1] != (byte)'/'))
                _position++;
            if (_position + 1 >= _data.Length)
                throw new InvalidDataException("The entity lump ended inside a block comment.");
            _position += 2;
        }
    }
}
