using System.Diagnostics;

namespace Meatcorps.AudioAnalyzer.Analysis;

internal sealed class BeatDetector
{
    private readonly float[] _history;
    private readonly float _thresholdMultiplier;
    private readonly float _minimumLowLevel;
    private readonly long _refractoryTicks;
    private int _historyIndex;
    private int _historyCount;
    private float _historySum;
    private float _previousLow;
    private long _lastBeatTicks;

    public BeatDetector(int historySize, float thresholdMultiplier, float minimumLowLevel, int refractoryMs)
    {
        _history = new float[Math.Max(8, historySize)];
        _thresholdMultiplier = thresholdMultiplier;
        _minimumLowLevel = minimumLowLevel;
        _refractoryTicks = (long)(Stopwatch.Frequency * (refractoryMs / 1000.0));
    }

    public bool Update(float lowLevel)
    {
        var flux = MathF.Max(0, lowLevel - _previousLow);
        _previousLow = lowLevel;

        var average = _historyCount == 0 ? 0 : _historySum / _historyCount;
        var now = Stopwatch.GetTimestamp();

        var enoughHistory = _historyCount >= Math.Min(16, _history.Length);
        var threshold = MathF.Max(0.0005f, average * _thresholdMultiplier);
        var beat = enoughHistory &&
                   lowLevel >= _minimumLowLevel &&
                   flux > threshold &&
                   now - _lastBeatTicks >= _refractoryTicks;

        if (beat)
            _lastBeatTicks = now;

        if (_historyCount < _history.Length)
        {
            _history[_historyIndex] = flux;
            _historySum += flux;
            _historyCount++;
        }
        else
        {
            _historySum -= _history[_historyIndex];
            _history[_historyIndex] = flux;
            _historySum += flux;
        }

        _historyIndex = (_historyIndex + 1) % _history.Length;
        return beat;
    }
}
