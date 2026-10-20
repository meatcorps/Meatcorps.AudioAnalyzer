using System.Net.WebSockets;

namespace Meatcorps.AudioAnalyzer.Client;

public sealed class AudioAnalyzerClient : IDisposable
{
    private readonly Uri _uri;
    private readonly TimeSpan _reconnectDelay;

    private CancellationTokenSource? _cts;
    private Task? _runner;
    private ClientWebSocket? _socket;

    // int so reads/writes are atomic and Volatile works nicely.
    private int _low;
    private int _mid;
    private int _high;
    private int _beatSequence;
    private int _connected;

    public byte Low => (byte)Volatile.Read(ref _low);
    public byte Mid => (byte)Volatile.Read(ref _mid);
    public byte High => (byte)Volatile.Read(ref _high);

    public float Low01 => Low / 255f;
    public float Mid01 => Mid / 255f;
    public float High01 => High / 255f;

    /// <summary>
    /// Increments whenever a new beat is received.
    /// Games can compare this with their previously seen value.
    /// </summary>
    public int BeatSequence => Volatile.Read(ref _beatSequence);

    public bool IsConnected => Volatile.Read(ref _connected) != 0;

    public AudioAnalyzerClient(
        string host,
        int port = 8765,
        TimeSpan? reconnectDelay = null)
        : this(
            new Uri($"ws://{host}:{port}/audio"),
            reconnectDelay)
    {
    }

    public AudioAnalyzerClient(
        Uri uri,
        TimeSpan? reconnectDelay = null)
    {
        _uri = uri;
        _reconnectDelay =
            reconnectDelay ?? TimeSpan.FromSeconds(1);
    }

    public void Start()
    {
        if (_runner != null)
            return;

        _cts = new CancellationTokenSource();
        _runner = RunAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        if (_cts == null)
            return;

        await _cts.CancelAsync();

        if (_runner != null)
        {
            try
            {
                await _runner;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _runner = null;

        _cts.Dispose();
        _cts = null;

        Volatile.Write(ref _connected, 0);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Analyzer disappeared, network changed, machine rebooted, etc.
            }

            Volatile.Write(ref _connected, 0);

            if (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(
                    _reconnectDelay,
                    cancellationToken);
            }
        }
    }

    private async Task ConnectAndReceiveAsync(
        CancellationToken cancellationToken)
    {
        _socket?.Dispose();
        _socket = new ClientWebSocket();

        await _socket.ConnectAsync(
            _uri,
            cancellationToken);

        Volatile.Write(ref _connected, 1);

        var buffer = new byte[4];

        while (_socket.State == WebSocketState.Open &&
               !cancellationToken.IsCancellationRequested)
        {
            var offset = 0;

            while (offset < buffer.Length)
            {
                var result = await _socket.ReceiveAsync(
                    buffer.AsMemory(offset),
                    cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                    return;

                if (result.MessageType != WebSocketMessageType.Binary)
                    continue;

                offset += result.Count;

                if (result.EndOfMessage)
                    break;
            }

            if (offset != 4)
                continue;

            Volatile.Write(ref _low, buffer[0]);
            Volatile.Write(ref _mid, buffer[1]);
            Volatile.Write(ref _high, buffer[2]);

            if ((buffer[3] & 0x01) != 0)
                Interlocked.Increment(ref _beatSequence);
        }
    }

    public void Dispose()
    {
        StopAsync().Wait(TimeSpan.FromSeconds(10));

        _socket?.Dispose();
        _socket = null;
    }
}