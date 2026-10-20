namespace Meatcorps.AudioAnalyzer.Analysis;

public sealed class AudioAnalysisState
{
    private readonly object _sync = new();
    private AudioAnalysisFrame _latest;

    public AudioAnalysisFrame Latest
    {
        get
        {
            lock (_sync)
                return _latest;
        }
    }

    public void Publish(AudioAnalysisFrame frame)
    {
        lock (_sync)
            _latest = frame;
    }
}
