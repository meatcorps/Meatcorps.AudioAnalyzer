namespace Meatcorps.AudioAnalyzer.Audio;

/// <summary>
/// Single-producer/single-consumer ring buffer. The MiniAudio callback is the
/// only writer and AudioAnalysisWorker is the only reader.
/// </summary>
public sealed class AudioSampleRingBuffer
{
    private readonly float[] _buffer;
    private readonly int _mask;
    private long _writePosition;
    private long _readPosition;

    public AudioSampleRingBuffer(int capacityPowerOfTwo = 262_144)
    {
        if (capacityPowerOfTwo < 2 || (capacityPowerOfTwo & (capacityPowerOfTwo - 1)) != 0)
            throw new ArgumentException("Capacity must be a power of two.", nameof(capacityPowerOfTwo));

        _buffer = new float[capacityPowerOfTwo];
        _mask = capacityPowerOfTwo - 1;
    }

    public int Available => (int)Math.Min(int.MaxValue,
        Volatile.Read(ref _writePosition) - Volatile.Read(ref _readPosition));

    public void Write(float sample)
    {
        var write = _writePosition;
        var read = Volatile.Read(ref _readPosition);

        // Never block the real-time audio thread. The producer must not move the
        // consumer index, so on overflow we drop the newest sample.
        if (write - read >= _buffer.Length)
            return;

        _buffer[write & _mask] = sample;
        Volatile.Write(ref _writePosition, write + 1);
    }

    public bool TryRead(Span<float> destination)
    {
        var read = _readPosition;
        var write = Volatile.Read(ref _writePosition);

        if (write - read < destination.Length)
            return false;

        for (var i = 0; i < destination.Length; i++)
            destination[i] = _buffer[(read + i) & _mask];

        Volatile.Write(ref _readPosition, read + destination.Length);
        return true;
    }
}
