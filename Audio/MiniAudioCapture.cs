using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Hexa.NET.MiniAudio;
using Microsoft.Extensions.Options;

namespace Meatcorps.AudioAnalyzer.Audio;

/// <summary>
/// Captures a MiniAudio input device as float PCM and writes a mono mix into
/// the SPSC ring buffer. CaptureDevice may be left empty for the OS default,
/// set to a numeric enumeration index, or set to a device-name substring.
/// </summary>
public sealed unsafe class MiniAudioCapture : IHostedService, IDisposable
{
    private readonly AudioSampleRingBuffer _ringBuffer;
    private readonly MaDeviceType _deviceType;
    private readonly AudioAnalyzerOptions _options;
    private MaContext* _context;
    private MaDevice* _device;
    private GCHandle _selfHandle;
    private bool _contextInitialized;
    private bool _deviceInitialized;

    public MiniAudioCapture(AudioSampleRingBuffer ringBuffer, IOptions<AudioAnalyzerOptions> options)
    {
        _ringBuffer = ringBuffer;
        _deviceType = options.Value.DeviceType;
        _options = options.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.Channels <= 0)
            throw new InvalidOperationException("AudioAnalyzer:Channels must be greater than zero.");

        if (_options.SampleRate <= 0)
            throw new InvalidOperationException("AudioAnalyzer:SampleRate must be greater than zero.");

        _context = (MaContext*)NativeMemory.AllocZeroed(
            (nuint)sizeof(MaContext));

        if (_context == null)
            throw new OutOfMemoryException();
        
        var result = MiniAudio.ContextInit(
            (MaBackend*)null,
            0,
            default(MaContextConfigPtr),
            _context);

        if (result != MaResult.Success)
            throw new InvalidOperationException($"MiniAudio ContextInit failed: {result}");

        _contextInitialized = true;

        try
        {
            var devices = EnumerateCaptureDevices();
            PrintCaptureDevices(devices);

            _selfHandle = GCHandle.Alloc(this);

            var config = MiniAudio.DeviceConfigInit(_deviceType);
            config.Capture.Format = MaFormat.F32;
            config.Capture.Channels = (uint)_options.Channels;
            config.SampleRate = (uint)_options.SampleRate;
            config.PUserData = (void*)GCHandle.ToIntPtr(_selfHandle);
            config.DataCallback = (void*)(delegate* unmanaged[Cdecl]<MaDevice*, void*, void*, uint, void>)&DataCallback;

            var selected = SelectCaptureDevice(devices, _options.CaptureDevice);
            MaDeviceId selectedId = default;

            if (selected is not null)
            {
                selectedId = selected.Id;
                config.Capture.PDeviceID = &selectedId;
            }
            
            _device = (MaDevice*)NativeMemory.AllocZeroed(
                (nuint)sizeof(MaDevice));

            if (_device == null)
                throw new OutOfMemoryException();

            result = MiniAudio.DeviceInit(_context, in config, _device);
            if (result != MaResult.Success)
                throw new InvalidOperationException($"MiniAudio DeviceInit failed: {result}");

            _deviceInitialized = true;

            result = MiniAudio.DeviceStart(_device);
            if (result != MaResult.Success)
                throw new InvalidOperationException($"MiniAudio DeviceStart failed: {result}");

            var selectedName = selected?.Name
                               ?? devices.FirstOrDefault(x => x.IsDefault)?.Name
                               ?? "OS default input";

            Console.WriteLine();
            Console.WriteLine($"Audio capture started: {selectedName}");
            Console.WriteLine($"Format: {_options.SampleRate} Hz, {_options.Channels} channel(s), float32");

            return Task.CompletedTask;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    private List<CaptureDeviceInfo> EnumerateCaptureDevices()
    {
        MaDeviceInfo* captureInfos = null;
        uint captureCount = 0;

        MaDeviceInfo* playbackDevices = null;
        uint playbackDeviceCount = 0;
        
        var result = MiniAudio.ContextGetDevices(
            _context,
            ref playbackDevices,
            ref playbackDeviceCount,
            ref captureInfos,
            ref captureCount);

        if (result != MaResult.Success)
            throw new InvalidOperationException($"MiniAudio ContextGetDevices failed: {result}");

        var devices = new List<CaptureDeviceInfo>(checked((int)captureCount));

        for (var i = 0; i < captureCount; i++)
        {
            var info = &captureInfos[i];
            devices.Add(new CaptureDeviceInfo(
                checked((int)i),
                ReadDeviceName(info),
                info->Id,
                info->IsDefault != 0));
        }
        
        for (var i = 0; i < playbackDeviceCount; i++)
        {
            var info = &playbackDevices[i];
            devices.Add(new CaptureDeviceInfo(
                checked((int)i),
                ReadDeviceName(info),
                info->Id,
                info->IsDefault != 0));
        }

        return devices;
    }

    private void PrintCaptureDevices(IReadOnlyList<CaptureDeviceInfo> devices)
    {
        Console.WriteLine($"Capture devices: ({_deviceType})");

        if (devices.Count == 0)
        {
            Console.WriteLine("  <none>");
            return;
        }

        foreach (var device in devices)
            Console.WriteLine($"  [{device.Index}] {device.Name}{(device.IsDefault ? " [DEFAULT]" : string.Empty)}");
    }

    private static CaptureDeviceInfo? SelectCaptureDevice(
        IReadOnlyList<CaptureDeviceInfo> devices,
        string? requestedDevice)
    {
        if (string.IsNullOrWhiteSpace(requestedDevice))
        {
            Console.WriteLine("CaptureDevice is not configured; MiniAudio will use the OS default input.");
            return null;
        }

        var requested = requestedDevice.Trim();

        if (int.TryParse(requested, out var requestedIndex))
        {
            var indexedDevice = devices.FirstOrDefault(x => x.Index == requestedIndex);
            if (indexedDevice is null)
                throw new InvalidOperationException(
                    $"Capture device index {requestedIndex} does not exist. See the enumerated device list above.");

            Console.WriteLine($"Selected capture device [{indexedDevice.Index}] {indexedDevice.Name}");
            return indexedDevice;
        }

        var exactMatches = devices
            .Where(x => string.Equals(x.Name, requested, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (exactMatches.Length == 1)
        {
            Console.WriteLine($"Selected capture device [{exactMatches[0].Index}] {exactMatches[0].Name}");
            return exactMatches[0];
        }

        var containsMatches = devices
            .Where(x => x.Name.Contains(requested, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (containsMatches.Length == 1)
        {
            Console.WriteLine($"Selected capture device [{containsMatches[0].Index}] {containsMatches[0].Name}");
            return containsMatches[0];
        }

        if (containsMatches.Length > 1)
        {
            var matches = string.Join(Environment.NewLine,
                containsMatches.Select(x => $"  [{x.Index}] {x.Name}"));

            throw new InvalidOperationException(
                $"CaptureDevice '{requested}' matches more than one input device:{Environment.NewLine}{matches}{Environment.NewLine}" +
                "Use a more specific name or a numeric index.");
        }

        throw new InvalidOperationException(
            $"CaptureDevice '{requested}' was not found. See the enumerated device list above.");
    }

    private static string ReadDeviceName(MaDeviceInfo* info)
    {
        var name = &info->Name_0;
        var length = 0;

        while (length < 256 && name[length] != 0)
            length++;

        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(name, length));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DataCallback(MaDevice* device, void* output, void* input, uint frameCount)
    {
        if (device == null)
        {
            Console.WriteLine("Something is wrong: device=null");
            return;
        }

        if (input == null || device->PUserData == null)
        {
            Console.WriteLine(
                $"Something is wrong: Type={device->Type}, " +
                $"output=0x{(nint)output:X}, " +
                $"input=0x{(nint)input:X}, frames={frameCount}");
            return;
        }

        var handle = GCHandle.FromIntPtr((nint)device->PUserData);
        if (handle.Target is not MiniAudioCapture capture)
            return;

        capture.WriteInput((float*)input, checked((int)frameCount));
    }

    private void WriteInput(float* input, int frameCount)
    {
        var channels = _options.Channels;

        for (var frame = 0; frame < frameCount; frame++)
        {
            var offset = frame * channels;
            var mono = 0.0f;

            for (var channel = 0; channel < channels; channel++)
                mono += input[offset + channel];

            _ringBuffer.Write(mono / channels);
        }
    }

    public void Dispose()
    {
        if (_device != null)
        {
            if (_deviceInitialized)
            {
                MiniAudio.DeviceUninit(_device);
                _deviceInitialized = false;
            }

            NativeMemory.Free(_device);
            _device = null;
        }

        // Only release this AFTER DeviceUninit, because the audio
        // callback can still use PUserData until the device has stopped.
        ReleaseHandle();

        if (_context != null)
        {
            if (_contextInitialized)
            {
                MiniAudio.ContextUninit(_context);
                _contextInitialized = false;
            }

            NativeMemory.Free(_context);
            _context = null;
        }
    }

    private void ReleaseHandle()
    {
        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
    }

    private sealed record CaptureDeviceInfo(int Index, string Name, MaDeviceId Id, bool IsDefault);
}
