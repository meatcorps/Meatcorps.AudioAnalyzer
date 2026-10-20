# Meatcorps.AudioAnalyzer

Standalone live audio analyzer for the Halloween arcade hall.

It captures the operating system's default recording/line-in device, analyzes the live PCM stream, and broadcasts a tiny music telemetry frame over WebSockets.

## Selecting the capture device

Every capture device is printed at startup. `AudioAnalyzer:CaptureDevice` can be:

- empty: use the OS default input
- a numeric enumeration index, e.g. `"2"` (handy for testing)
- an exact device name or unique case-insensitive name substring, e.g. `"USB Audio CODEC"` (recommended for the arcade hall)

Persistent configuration in `appsettings.json`:

```json
"AudioAnalyzer": {
  "CaptureDevice": "USB Audio CODEC"
}
```

Or override it from the command line without editing the file:

```bash
dotnet run -- --AudioAnalyzer:CaptureDevice="USB Audio CODEC"
```

If a name matches multiple devices the analyzer refuses to guess and prints the matches.


## Signal path

`Mixer AUX/REC OUT -> audio interface line input -> MiniAudio capture -> FFT -> WebSocket`

## WebSocket

URL: `ws://<machine>:8765/audio`

Every WebSocket binary message is exactly four bytes:

| Byte | Meaning |
| --- | --- |
| 0 | Low frequency level, 0..255 |
| 1 | Mid frequency level, 0..255 |
| 2 | High frequency level, 0..255 |
| 3 | Flags; bit 0 is a one-frame beat pulse |

The service broadcasts at 30 Hz by default. Beat is latched internally so one detected beat becomes one outgoing WebSocket beat packet even though FFT analysis runs faster than the network update rate.

## Frequency bands

Defaults:

- Low: 20-250 Hz
- Mid: 250-4000 Hz
- High: 4000-16000 Hz

The FFT uses a 2048-sample Hann window and a 512-sample hop.

## First hardware test

1. Connect an AUX/REC output from the central mixer to a proper line input / USB audio interface.
2. Select that line input as the operating system's default recording device.
3. Run the project.
4. Open `http://localhost:8765/`.
5. Play music through the mixer and verify LOW/MID/HIGH movement and BEAT flashes.

The first version intentionally uses the default capture device. Device enumeration/selection should be the next step after the capture + analysis path is proven on the actual mixer hardware.

## C# client

```csharp
using System.Net.WebSockets;

using var socket = new ClientWebSocket();
await socket.ConnectAsync(new Uri("ws://music-analyzer:8765/audio"), CancellationToken.None);

var buffer = new byte[4];
while (socket.State == WebSocketState.Open)
{
    var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
    if (result.MessageType != WebSocketMessageType.Binary || result.Count < 4)
        continue;

    var low = buffer[0];
    var mid = buffer[1];
    var high = buffer[2];
    var beat = (buffer[3] & 1) != 0;
}
```
