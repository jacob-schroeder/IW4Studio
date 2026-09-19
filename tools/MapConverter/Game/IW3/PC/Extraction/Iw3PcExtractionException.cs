namespace MapConverter.Game.IW3.PC.Extraction;

internal sealed class Iw3PcExtractionException : IOException
{
    public Iw3PcExtractionException(string message)
        : base(message)
    {
    }

    public Iw3PcExtractionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
