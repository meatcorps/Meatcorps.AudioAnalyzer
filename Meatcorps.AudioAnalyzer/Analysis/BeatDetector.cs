using System.Diagnostics;

internal sealed class BeatDetector
{
    private readonly float[] _history;
    private readonly float _thresholdMultiplier;
    private readonly float _minimumLowLevel;
    private readonly float _minimumFlux;
    private readonly long _refractoryTicks;

    private int _historyIndex;
    private int _historyCount;

    private float _historySum;
    private float _historySquareSum;

    private float _previousLow;
    private long _lastBeatTicks;

    public BeatDetector(
        int historySize,
        float thresholdMultiplier,
        float minimumLowLevel,
        float minimumFlux,
        int refractoryMs)
    {
        _history = new float[Math.Max(8, historySize)];
        _thresholdMultiplier = thresholdMultiplier;
        _minimumLowLevel = minimumLowLevel;
        _minimumFlux = minimumFlux;

        _refractoryTicks =
            (long)(Stopwatch.Frequency * (refractoryMs / 1000.0));
    }

    public bool Update(float lowLevel)
    {
        var flux = MathF.Max(0, lowLevel - _previousLow);
        _previousLow = lowLevel;

        var average =
            _historyCount == 0
                ? 0
                : _historySum / _historyCount;

        var variance =
            _historyCount == 0
                ? 0
                : MathF.Max(
                    0,
                    (_historySquareSum / _historyCount) -
                    average * average);

        var deviation = MathF.Sqrt(variance);

        var threshold = MathF.Max(
            _minimumFlux,
            average + deviation * _thresholdMultiplier);

        var now = Stopwatch.GetTimestamp();

        var enoughHistory =
            _historyCount >= Math.Min(16, _history.Length);

        var beat =
            enoughHistory &&
            lowLevel >= _minimumLowLevel &&
            flux >= threshold &&
            now - _lastBeatTicks >= _refractoryTicks;

        if (beat)
            _lastBeatTicks = now;

        if (_historyCount < _history.Length)
        {
            _history[_historyIndex] = flux;

            _historySum += flux;
            _historySquareSum += flux * flux;

            _historyCount++;
        }
        else
        {
            var previous = _history[_historyIndex];

            _historySum -= previous;
            _historySquareSum -= previous * previous;

            _history[_historyIndex] = flux;

            _historySum += flux;
            _historySquareSum += flux * flux;
        }

        _historyIndex =
            (_historyIndex + 1) % _history.Length;

        return beat;
    }
}