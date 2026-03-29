using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MarketSocket
{
    // ─────────────────────────────────────────────────────────────────────────
    // Implementation of the IMarketDataSocketClient interface, which defines handlers for
    // various message codes. The handlers in this example just print the raw data to the console and log it at Debug level.
    // ─────────────────────────────────────────────────────────────────────────
    public class XtsMarketDataSocketClient : XtsApiClient.IMarketDataSocketClient
    {
        private readonly ILogger<XtsMarketDataSocketClient> _logger;

        public XtsMarketDataSocketClient(ILogger<XtsMarketDataSocketClient> logger)
        {
            _logger = logger;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public Task OnConnectAsync()
        {
            Console.WriteLine("Market Data Socket connected successfully!");
            _logger.LogInformation("Market Data Socket connected.");
            return Task.CompletedTask;
        }

        public Task OnDisconnectAsync()
        {
            Console.WriteLine("Market Data Socket disconnected!");
            _logger.LogInformation("Market Data Socket disconnected.");
            return Task.CompletedTask;
        }

        public Task OnMessageAsync(string data)
        {
            Console.WriteLine("I received a message!" + data);
            _logger.LogDebug("message: {Data}", data);
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(string data)
        {
            Console.WriteLine("I received an error!" + data);
            _logger.LogError("Socket error: {Data}", data);
            return Task.CompletedTask;
        }

        // ── Full events ───────────────────────────────────────────────────────

        public Task OnEventTouchlineFullAsync(string data)
        {
            Console.WriteLine("I received a 1501 Level1,Touchline message!" + data);
            _logger.LogDebug("1501-full: {Data}", data);
            return Task.CompletedTask;
        }

        public Task OnEventMarketDataFullAsync(string data)
        {
            Console.WriteLine("I received a 1502 Market depth message!" + data);
            _logger.LogDebug("1502-full: {Data}", data);
            return Task.CompletedTask;
        }

        public Task OnEventCandleDataFullAsync(string data)
        {
            Console.WriteLine("I received a 1505 Candle data message!" + data);
            _logger.LogDebug("1505-full: {Data}", data);
            return Task.CompletedTask;
        }

        public Task OnEventMarketStatusFullAsync(string data)
        {
            Console.WriteLine("I received a 1507 MarketStatus message!" + data);
            _logger.LogDebug("1507-full: {Data}", data);
            return Task.CompletedTask;
        }

        public Task OnEventOpenInterestFullAsync(string data)
        {
            Console.WriteLine("I received a 1510 Open interest message!" + data);
            _logger.LogDebug("1510-full: {Data}", data);
            return Task.CompletedTask;
        }

        public Task OnEventLastTradedPriceFullAsync(string data)
        {
            Console.WriteLine("I received a 1512 LTP message!" + data);
            _logger.LogDebug("1512-full: {Data}", data);
            return Task.CompletedTask;
        }

        public Task OnEventInstrumentChangeFullAsync(string data)
        {
            Console.WriteLine($"I received a 1105:Instrument Change full: {data}");
            _logger.LogDebug("1105-full: {Data}", data);
            return Task.CompletedTask;
        }

        // ── Partial events ────────────────────────────────────────────────────

        public Task OnEventTouchlinePartialAsync(string data)
        {
            Console.WriteLine("I received a 1501 Level1,Touchline partial message!" + data);
            _logger.LogDebug("1501-partial: {Data}", data);
            return Task.CompletedTask;
        }

        public Task OnEventMarketDataPartialAsync(string data)
        {
            Console.WriteLine("I received a 1502 Market depth partial message!" + data);
            _logger.LogDebug("1502-partial: {Data}", data);
            return Task.CompletedTask;
        }

        public Task OnEventCandleDataPartialAsync(string data)
        {
            Console.WriteLine("I received a 1505 Candle data partial message!" + data);
            _logger.LogDebug("1505-partial: {Data}", data);
            return Task.CompletedTask;
        }

        public Task OnEventOpenInterestPartialAsync(string data)
        {
            Console.WriteLine("I received a 1510 Open interest partial message!" + data);
            _logger.LogDebug("1510-partial: {Data}", data);
            return Task.CompletedTask;
        }

        public Task OnEventLastTradedPricePartialAsync(string data)
        {
            Console.WriteLine("I received a 1512 LTP partial message!" + data);
            _logger.LogDebug("1512-partial: {Data}", data);
            return Task.CompletedTask;
        }

        public Task OnEventInstrumentChangePartialAsync(string data)
        {
            Console.WriteLine($"I received a 1105:Instrument Change partial: {data}");
            _logger.LogDebug("1105-partial: {Data}", data);
            return Task.CompletedTask;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MarketDataStreamer — thin static helper called from Program.cs.
    // Accepts the already-logged-in XtsConnect instance so it reuses the
    // active session without logging in again.
    // ─────────────────────────────────────────────────────────────────────────
    public static class MarketDataStreamer
    {
        /// <summary>
        /// Connects the socket, subscribes to the requested message codes,
        /// streams for <paramref name="streamDurationSeconds"/> seconds, then
        /// disconnects. Login and logout are the caller's responsibility.
        /// </summary>
        /// <param name="xt">
        ///     An already-logged-in <see cref="XtsApiClient.XtsConnect"/> instance.
        ///     Its <c>Token</c> and <c>UserID</c> properties must be populated
        ///     (they are set automatically by <c>MarketdataLoginAsync()</c>).
        /// </param>
        /// <param name="apiRoot">Base URL of the XTS server.</param>
        /// <param name="instruments">Instruments to subscribe to.</param>
        /// <param name="streamDurationSeconds">How long to stream before returning.</param>
        public static async Task RunAsync(
            XtsApiClient.XtsConnect       xt,
            string                        apiRoot,
            List<Dictionary<string, int>> instruments,
            int                           streamDurationSeconds = 50)
        {
            using var loggerFactory = LoggerFactory.Create(b =>
                b.AddConsole().SetMinimumLevel(LogLevel.Debug));

            var handlerLogger = loggerFactory.CreateLogger<XtsMarketDataSocketClient>();
            var socketLogger  = loggerFactory.CreateLogger<XtsApiClient.MDSocketIo>();
            var handler = new XtsMarketDataSocketClient(handlerLogger);

            await using var socket = new XtsApiClient.MDSocketIo(
                token:   xt.Token!,
                userID:  xt.UserID!,
                rootUrl: apiRoot,
                handler: handler,
                logger:  socketLogger);

            await socket.ConnectAsync();

            // ── Subscriptions ─────────────────────────────────────────────────

            var r1501 = await xt.SendSubscriptionAsync(instruments, 1501);
            Console.WriteLine($"Subscription 1501: {Serialize(r1501)}\n\n\n");

            var r1502 = await xt.SendSubscriptionAsync(instruments, 1502);
            Console.WriteLine($"Subscription 1502: {Serialize(r1502)}\n\n\n");

            var r1510 = await xt.SendSubscriptionAsync(instruments, 1510);
            Console.WriteLine($"Subscription 1510: {Serialize(r1510)}\n\n\n");

            var r1512 = await xt.SendSubscriptionAsync(instruments, 1512);
            Console.WriteLine($"Subscription 1512: {Serialize(r1512)}\n\n\n");

            // ── Stream ────────────────────────────────────────────────────────
            Console.WriteLine($"Streaming for {streamDurationSeconds} seconds…");
            await Task.Delay(TimeSpan.FromSeconds(streamDurationSeconds));

            await socket.DisconnectAsync();
            Console.WriteLine("Socket disconnected.");
        }

        private static string Serialize(object? obj) =>
            JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = false });
    }
}