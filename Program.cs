using Meatcorps.AudioAnalyzer;
using Meatcorps.AudioAnalyzer.Analysis;
using Meatcorps.AudioAnalyzer.Audio;
using Meatcorps.AudioAnalyzer.Network;

var builder = WebApplication.CreateBuilder(args);

Console.WriteLine(
    $"RAW CONFIG BroadcastHz = '{builder.Configuration["AudioAnalyzer:BroadcastHz"]}'");

builder.WebHost.UseUrls("http://0.0.0.0:8765");
builder.Services
    .AddOptions<AudioAnalyzerOptions>()
    .Bind(builder.Configuration.GetSection(AudioAnalyzerOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<AudioSampleRingBuffer>();
builder.Services.AddSingleton<AudioAnalysisState>();

builder.Services.AddSingleton<MiniAudioCapture>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<MiniAudioCapture>());

builder.Services.AddHostedService<AudioAnalysisWorker>();

builder.Services.AddSingleton<AudioWebSocketHub>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<AudioWebSocketHub>());

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20)
});

app.Map("/audio", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket connection required.");
        return;
    }

    var socket = await context.WebSockets.AcceptWebSocketAsync();
    var hub = context.RequestServices.GetRequiredService<AudioWebSocketHub>();
    await hub.AddClientAsync(socket, context.RequestAborted);
});

app.MapGet("/state", (AudioAnalysisState state, AudioWebSocketHub hub) =>
{
    var frame = state.Latest;
    return Results.Ok(new
    {
        low = frame.Low,
        mid = frame.Mid,
        high = frame.High,
        beatSequence = frame.BeatSequence,
        clients = hub.ClientCount
    });
});

Console.WriteLine("Meatcorps Audio Analyzer");
Console.WriteLine("Monitor:   http://localhost:8765/");
Console.WriteLine("WebSocket: ws://localhost:8765/audio");
Console.WriteLine("Protocol:  [low][mid][high][flags], flags bit 0 = beat");

await app.RunAsync();
