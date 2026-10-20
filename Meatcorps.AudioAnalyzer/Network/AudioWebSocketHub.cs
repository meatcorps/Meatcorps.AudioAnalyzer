using System.Collections.Concurrent;
using System.Net.WebSockets;
using Meatcorps.AudioAnalyzer.Analysis;
using Microsoft.Extensions.Options;

namespace Meatcorps.AudioAnalyzer.Network;

public sealed class AudioWebSocketHub : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private readonly AudioAnalysisState _state;
    private readonly AudioAnalyzerOptions _options;
    private readonly int _broadcastHz;
    private uint _lastBeatSequence;
    private readonly Guid _instanceId = Guid.NewGuid();

    public AudioWebSocketHub(AudioAnalysisState state, IOptions<AudioAnalyzerOptions> options)
    {
        _state = state;
        
        Console.WriteLine($"IOptions value BroadcastHz: {options.Value.BroadcastHz}");

        _options = options.Value;

        _broadcastHz = _options.BroadcastHz;
    }

    public int ClientCount => _clients.Count;

    public async Task AddClientAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        _clients[id] = socket;
        Console.WriteLine($"WebSocket connected: {id} ({_clients.Count} client(s))");

        var receiveBuffer = new byte[128];

        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(receiveBuffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            _clients.TryRemove(id, out _);

            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", CancellationToken.None);
                }
                catch
                {
                    // Client disappeared; there is nothing left to close cleanly.
                }
            }

            socket.Dispose();
            Console.WriteLine($"WebSocket disconnected: {id} ({_clients.Count} client(s))");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(1.0 / Math.Max(1, _broadcastHz));
        Console.WriteLine($"Audio analysis interval: {interval} {_broadcastHz}");
        using var timer = new PeriodicTimer(interval);

        var payload = new byte[4];

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var current = _state.Latest;
            var isNewBeat = current.BeatSequence != _lastBeatSequence;
            _lastBeatSequence = current.BeatSequence;

            var frame = current with { Beat = isNewBeat };
            frame.WriteBinary(payload);

            foreach (var pair in _clients)
            {
                var socket = pair.Value;
                if (socket.State != WebSocketState.Open)
                    continue;

                try
                {
                    await socket.SendAsync(
                        payload,
                        WebSocketMessageType.Binary,
                        endOfMessage: true,
                        stoppingToken);
                }
                catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
                {
                    _clients.TryRemove(pair.Key, out _);
                }
            }
        }
    }
}
