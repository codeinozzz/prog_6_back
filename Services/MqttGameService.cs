using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace BattleTanks_Backend.Services;

// ---- Event payloads ----

public record PowerUpEvent(string Id, string Type, double X, double Y, string RoomId, long Timestamp);
public record CollisionEvent(string AttackerId, string VictimId, string RoomId, int Damage, long Timestamp);
public record GameEndEvent(string RoomId, string WinnerId, string WinnerName, long Timestamp);
public record ChatMqttEvent(string RoomId, string Sender, string Message, long Timestamp);

// ---- Interface ----

public interface IMqttGameService
{
    Task PublishPowerUpSpawnedAsync(string roomId, PowerUpEvent powerUp);
    Task PublishPowerUpCollectedAsync(string roomId, string powerUpId, string collectorId);
    Task PublishCollisionAsync(string roomId, CollisionEvent collision);
    Task PublishGameEndAsync(string roomId, string winnerId, string winnerName);
    Task PublishChatAsync(string roomId, string sender, string message);
    bool IsConnected { get; }
}

// ---- Implementation ----

public class MqttGameService : IMqttGameService, IHostedService, IAsyncDisposable
{
    private readonly IMqttClient _client;
    private readonly MqttClientOptions _options;
    private readonly string _topicPrefix;
    private readonly ILogger<MqttGameService> _logger;

    public bool IsConnected => _client.IsConnected;

    public MqttGameService(IConfiguration config, ILogger<MqttGameService> logger)
    {
        _logger = logger;
        _topicPrefix = config["Mqtt:TopicPrefix"] ?? "battletanks";

        var factory = new MqttFactory();
        _client = factory.CreateMqttClient();

        _options = new MqttClientOptionsBuilder()
            .WithTcpServer(config["Mqtt:Host"] ?? "localhost", config.GetValue<int>("Mqtt:Port", 1883))
            .WithClientId(config["Mqtt:ClientId"] ?? "BattleTanks-Backend")
            .WithCleanSession(true)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .Build();
    }

    // IHostedService lifecycle

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _client.ConnectAsync(_options, cancellationToken);
            _logger.LogInformation("[MQTT] Connected to broker at {Host}", _options.ChannelOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[MQTT] Could not connect to broker: {Msg}. Events will be skipped.", ex.Message);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client.IsConnected)
            await _client.DisconnectAsync(cancellationToken: cancellationToken);
    }

    // ---- Publish helpers ----

    private async Task PublishAsync(string topic, object payload, MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtLeastOnce)
    {
        if (!_client.IsConnected)
        {
            _logger.LogWarning("[MQTT] Not connected — skipping publish to {Topic}", topic);
            return;
        }

        var json = JsonSerializer.Serialize(payload);
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(json))
            .WithQualityOfServiceLevel(qos)
            .WithRetainFlag(false)
            .Build();

        await _client.PublishAsync(message, CancellationToken.None);
        _logger.LogInformation("[MQTT] Published to {Topic}: {Payload}", topic, json);
    }

    // ---- Public API ----

    public Task PublishPowerUpSpawnedAsync(string roomId, PowerUpEvent powerUp) =>
        PublishAsync(
            $"{_topicPrefix}/room/{roomId}/powerup/spawned",
            powerUp,
            MqttQualityOfServiceLevel.AtLeastOnce);

    public Task PublishPowerUpCollectedAsync(string roomId, string powerUpId, string collectorId) =>
        PublishAsync(
            $"{_topicPrefix}/room/{roomId}/powerup/collected",
            new { powerUpId, collectorId, roomId, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
            MqttQualityOfServiceLevel.AtLeastOnce);

    public Task PublishCollisionAsync(string roomId, CollisionEvent collision) =>
        PublishAsync(
            $"{_topicPrefix}/room/{roomId}/collision",
            collision,
            MqttQualityOfServiceLevel.AtMostOnce);   // QoS 0 — alta frecuencia

    public Task PublishGameEndAsync(string roomId, string winnerId, string winnerName) =>
        PublishAsync(
            $"{_topicPrefix}/room/{roomId}/game/end",
            new GameEndEvent(roomId, winnerId, winnerName, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            MqttQualityOfServiceLevel.ExactlyOnce);  // QoS 2 — evento crítico

    public Task PublishChatAsync(string roomId, string sender, string message) =>
        PublishAsync(
            $"{_topicPrefix}/room/{roomId}/chat",
            new ChatMqttEvent(roomId, sender, message, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            MqttQualityOfServiceLevel.AtLeastOnce);

    public async ValueTask DisposeAsync()
    {
        if (_client.IsConnected)
            await _client.DisconnectAsync();
        _client.Dispose();
    }
}
