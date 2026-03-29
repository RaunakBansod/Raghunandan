using Microsoft.Extensions.Logging;
using SIOClient = SocketIOClient.SocketIO;
using SIOOptions = SocketIOClient.SocketIOOptions;
using SIOResponse = SocketIOClient.SocketIOResponse;
using SocketIOClient.Transport;   //+ provides TransportProtocol enum
using SocketIO.Core;

namespace XtsApiClient
{
    /// <summary>
    /// Socket.IO client for the XTS Market Data streaming API.
    /// Mirrors the Python MDSocket_io class in market_data_socket.py.
    /// </summary>
    public class MDSocketIo : IAsyncDisposable
    {
        private readonly SIOClient _client;
        private readonly IMarketDataSocketClient _handler;
        private readonly ILogger<MDSocketIo> _logger;
        private readonly string _connectionUrl;

        public string Token { get; }
        public string UserID { get; }
        public string RootUrl { get; }
        public string PublishFormat { get; } = "JSON";
        public string BroadcastMode { get; } = "Full";

        public MDSocketIo(
            string token,
            string userID,
            string rootUrl,
            IMarketDataSocketClient handler,
            bool reconnection = true,
            int reconnectionAttempts = 2,
            int reconnectionDelay = 1_000,
            int reconnectionDelayMax = 50_000,
            ILogger<MDSocketIo>? logger = null)
        {
            Token = token;
            UserID = userID;
            RootUrl = rootUrl;
            _handler = handler;
            _logger = logger ?? LoggerFactory
                           .Create(b => b.AddConsole())
                           .CreateLogger<MDSocketIo>();

            _connectionUrl = rootUrl.TrimEnd('/');

            _client = new SIOClient(_connectionUrl, new SIOOptions
            {
                Path = "/apimarketdata/socket.io",
                EIO = EngineIO.V3,
                Transport = TransportProtocol.WebSocket,
                Reconnection = reconnection,
                ReconnectionAttempts = reconnectionAttempts == 0 ? 5 : reconnectionAttempts,
                ReconnectionDelay = reconnectionDelay,
                ReconnectionDelayMax = reconnectionDelayMax,
                ConnectionTimeout = TimeSpan.FromSeconds(30),
                Query = new Dictionary<string, string>
                {
                    { "token", token },
                    { "userID", userID },
                    { "publishFormat", PublishFormat },
                    { "broadcastMode", BroadcastMode }
                }
            });

            RegisterEvents();
        }

        // ── Public API ────────────────────────────────────────────────────────

        public async Task ConnectAsync()
        {
            _logger.LogInformation("Connecting to {Url}", _connectionUrl);
            await _client.ConnectAsync();
        }

        public async Task DisconnectAsync()
        {
            _logger.LogInformation("Disconnecting from market data socket.");
            await _client.DisconnectAsync();
        }

        /// <summary>Registers a socket event with logging and invokes the async callback.</summary>
        private void On(string eventName, Func<SIOResponse, Task> callback)
        {
            _client.On(eventName, async response =>
            {
                var raw = Str(response);
                _logger.LogDebug("{Event}: {Data}", eventName, raw);
                await callback(response);
            });
        }

        // ── Event wiring —-────────────────────────────────────────────────
        private void RegisterEvents()
        {
            _client.OnConnected += async (_, _) =>
            {
                _logger.LogInformation("Socket connected.");
                await _handler.OnConnectAsync();
            };

            _client.OnDisconnected += async (_, reason) =>
            {
                _logger.LogInformation("Socket disconnected: {Reason}", reason);
                await _handler.OnDisconnectAsync();
            };

            _client.OnError += async (_, error) =>
            {
                _logger.LogError("Socket error: {Error}", error);
                await _handler.OnErrorAsync(error);
            };

            On("message", r => _handler.OnMessageAsync(Str(r)));
            On("1501-json-full", r => _handler.OnEventTouchlineFullAsync(Str(r)));
            On("1501-json-partial", r => _handler.OnEventTouchlinePartialAsync(Str(r)));
            On("1502-json-full", r => _handler.OnEventMarketDataFullAsync(Str(r)));
            On("1502-json-partial", r => _handler.OnEventMarketDataPartialAsync(Str(r)));
            On("1505-json-full", r => _handler.OnEventCandleDataFullAsync(Str(r)));
            On("1505-json-partial", r => _handler.OnEventCandleDataPartialAsync(Str(r)));
            On("1507-json-full", r => _handler.OnEventMarketStatusFullAsync(Str(r)));
            On("1510-json-full", r => _handler.OnEventOpenInterestFullAsync(Str(r)));
            On("1510-json-partial", r => _handler.OnEventOpenInterestPartialAsync(Str(r)));
            On("1512-json-full", r => _handler.OnEventLastTradedPriceFullAsync(Str(r)));
            On("1512-json-partial", r => _handler.OnEventLastTradedPricePartialAsync(Str(r)));
            On("1105-json-full", r => _handler.OnEventInstrumentChangeFullAsync(Str(r)));
            On("1105-json-partial", r => _handler.OnEventInstrumentChangePartialAsync(Str(r)));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Extracts the raw JSON string from a SocketIOResponse.
        /// Tries GetValue&lt;string&gt;(0) first; falls back to ToString().
        /// </summary>
        private static string Str(SIOResponse response)
        {
            try { return response.GetValue<string>(0) ?? response.ToString(); }
            catch { return response.ToString(); }
        }

        public async ValueTask DisposeAsync()
        {
            try { await _client.DisconnectAsync(); } catch { /* already disconnected */ }
            _client.Dispose();
        }

        public override string ToString() =>
            $"MDSocketIo(token={Token}, userID={UserID}, rootUrl={RootUrl})";
    }
}