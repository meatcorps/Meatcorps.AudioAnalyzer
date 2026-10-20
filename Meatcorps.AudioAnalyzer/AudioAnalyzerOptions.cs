using Hexa.NET.MiniAudio;

namespace Meatcorps.AudioAnalyzer;

public sealed class AudioAnalyzerOptions
{
    public const string SectionName = "AudioAnalyzer";

    public string? CaptureDevice { get; set; }

    public int SampleRate { get; set; } = 48_000;
    public int Channels { get; set; } = 2;
    public int FftSize { get; set; } = 2048;
    public int HopSize { get; set; } = 512;

    public float LowMaxHz { get; set; } = 250.0f;
    public float MidMaxHz { get; set; } = 4_000.0f;
    public float HighMaxHz { get; set; } = 16_000.0f;

    public float FloorDb { get; set; } = -60.0f;
    public float CeilingDb { get; set; } = -6.0f;

    public float Attack { get; set; } = 0.65f;
    public float Release { get; set; } = 0.12f;

    public float BeatThresholdMultiplier { get; set; } = 2.2f;
    public float BeatMinimumLowLevel { get; set; } = 0.15f;
    public int BeatRefractoryMs { get; set; } = 140;

    public int BroadcastHz { get; set; } = 30;

    public MaDeviceType DeviceType { get; set; } = MaDeviceType.Capture;
}
