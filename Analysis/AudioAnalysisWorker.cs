using System.Numerics;
using Meatcorps.AudioAnalyzer.Audio;
using Microsoft.Extensions.Options;

namespace Meatcorps.AudioAnalyzer.Analysis;

public sealed class AudioAnalysisWorker : BackgroundService
{
    private readonly AudioSampleRingBuffer _ringBuffer;
    private readonly AudioAnalysisState _state;
    private readonly AudioAnalyzerOptions _options;
    private readonly float[] _window;
    private readonly float[] _hop;
    private readonly Complex[] _fft;
    private readonly BeatDetector _beatDetector;
    private float _low;
    private float _mid;
    private float _high;
    private uint _beatSequence;

    public AudioAnalysisWorker(
        AudioSampleRingBuffer ringBuffer,
        AudioAnalysisState state,
        IOptions<AudioAnalyzerOptions> options)
    {
        _ringBuffer = ringBuffer;
        _state = state;
        _options = options.Value;

        if (_options.FftSize <= 0 || (_options.FftSize & (_options.FftSize - 1)) != 0)
            throw new InvalidOperationException("AudioAnalyzer:FftSize must be a power of two.");
        if (_options.HopSize <= 0 || _options.HopSize > _options.FftSize)
            throw new InvalidOperationException("AudioAnalyzer:HopSize must be > 0 and <= FftSize.");

        _window = new float[_options.FftSize];
        _hop = new float[_options.HopSize];
        _fft = new Complex[_options.FftSize];

        var analysesPerSecond = Math.Max(1, _options.SampleRate / _options.HopSize);
        _beatDetector = new BeatDetector(
            analysesPerSecond,
            _options.BeatThresholdMultiplier,
            _options.BeatMinimumLowLevel,
            _options.BeatRefractoryMs);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Prime one complete FFT window before producing values.
        while (!stoppingToken.IsCancellationRequested && !_ringBuffer.TryRead(_window))
            await Task.Delay(2, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_ringBuffer.TryRead(_hop))
            {
                await Task.Delay(1, stoppingToken);
                continue;
            }

            Array.Copy(_window, _options.HopSize, _window, 0, _options.FftSize - _options.HopSize);
            Array.Copy(_hop, 0, _window, _options.FftSize - _options.HopSize, _options.HopSize);

            Analyze();
        }
    }

    private void Analyze()
    {
        var n = _options.FftSize;

        for (var i = 0; i < n; i++)
        {
            // Hann window.
            var hann = 0.5f * (1.0f - MathF.Cos(2.0f * MathF.PI * i / (n - 1)));
            _fft[i] = new Complex(_window[i] * hann, 0);
        }

        FastFourierTransform.Transform(_fft);

        var lowRaw = GetBandAmplitude(20.0f, _options.LowMaxHz);
        var midRaw = GetBandAmplitude(_options.LowMaxHz, _options.MidMaxHz);
        var highRaw = GetBandAmplitude(_options.MidMaxHz, MathF.Min(_options.HighMaxHz, _options.SampleRate * 0.5f));

        var lowNormalized = ToNormalizedDb(lowRaw);
        var midNormalized = ToNormalizedDb(midRaw);
        var highNormalized = ToNormalizedDb(highRaw);

        _low = Smooth(_low, lowNormalized);
        _mid = Smooth(_mid, midNormalized);
        _high = Smooth(_high, highNormalized);

        // Beat detection uses the unsmoothed, normalized low band. This makes
        // the adaptive threshold much less dependent on audio-interface gain.
        var beat = _beatDetector.Update(lowNormalized);
        if (beat)
            _beatSequence++;

        _state.Publish(new AudioAnalysisFrame(
            ToByte(_low),
            ToByte(_mid),
            ToByte(_high),
            beat,
            _beatSequence));
    }

    private float GetBandAmplitude(float minHz, float maxHz)
    {
        var binWidth = _options.SampleRate / (float)_options.FftSize;
        var first = Math.Clamp((int)MathF.Ceiling(minHz / binWidth), 1, _fft.Length / 2);
        var last = Math.Clamp((int)MathF.Floor(maxHz / binWidth), first, _fft.Length / 2);

        double power = 0;
        var count = 0;

        for (var bin = first; bin <= last; bin++)
        {
            var magnitude = 2.0 * _fft[bin].Magnitude / _options.FftSize;
            power += magnitude * magnitude;
            count++;
        }

        return count == 0 ? 0 : (float)Math.Sqrt(power / count);
    }

    private float ToNormalizedDb(float amplitude)
    {
        var db = 20.0f * MathF.Log10(MathF.Max(amplitude, 0.000001f));
        return Math.Clamp(
            (db - _options.FloorDb) / (_options.CeilingDb - _options.FloorDb),
            0.0f,
            1.0f);
    }

    private float Smooth(float current, float target)
    {
        var factor = target > current ? _options.Attack : _options.Release;
        return current + (target - current) * factor;
    }

    private static byte ToByte(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255.0f), 0, 255);
}
