namespace Meatcorps.AudioAnalyzer.Analysis;

public readonly record struct AudioAnalysisFrame(
    byte Low,
    byte Mid,
    byte High,
    bool Beat,
    uint BeatSequence)
{
    public byte Flags => Beat ? (byte)1 : (byte)0;

    public void WriteBinary(Span<byte> destination)
    {
        if (destination.Length < 4)
            throw new ArgumentException("Audio frame requires four bytes.", nameof(destination));

        destination[0] = Low;
        destination[1] = Mid;
        destination[2] = High;
        destination[3] = Flags;
    }
}
