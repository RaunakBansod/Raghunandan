namespace XtsApiClient
{
    /// <summary>
    /// Callback interface that callers must implement to receive socket events.
    /// </summary>
    public interface IMarketDataSocketClient
    {
        // ── Connection lifecycle ──────────────────────────────────────────────

        /// <summary>Fired when the socket successfully connects.</summary>
        Task OnConnectAsync();

        /// <summary>Fired when the socket disconnects.</summary>
        Task OnDisconnectAsync();

        /// <summary>Fired on a generic socket message.</summary>
        Task OnMessageAsync(string data);

        /// <summary>Fired on any socket error.</summary>
        Task OnErrorAsync(string data);

        // ── Full broadcast events ─────────────────────────────────────────────

        /// <summary>1501-json-full — Touchline (Level 1) full snapshot.</summary>
        Task OnEventTouchlineFullAsync(string data);

        /// <summary>1502-json-full — Market Depth full snapshot.</summary>
        Task OnEventMarketDataFullAsync(string data);

        /// <summary>1505-json-full — Candle Data full snapshot.</summary>
        Task OnEventCandleDataFullAsync(string data);

        /// <summary>1507-json-full — Market Status full snapshot.</summary>
        Task OnEventMarketStatusFullAsync(string data);

        /// <summary>1510-json-full — Open Interest full snapshot.</summary>
        Task OnEventOpenInterestFullAsync(string data);

        /// <summary>1512-json-full — Last Traded Price full snapshot.</summary>
        Task OnEventLastTradedPriceFullAsync(string data);

        /// <summary>1105-json-full — Instrument Change full snapshot.</summary>
        Task OnEventInstrumentChangeFullAsync(string data);

        // ── Partial / delta events ────────────────────────────────────────────

        /// <summary>1501-json-partial — Touchline partial update.</summary>
        Task OnEventTouchlinePartialAsync(string data);

        /// <summary>1502-json-partial — Market Depth partial update.</summary>
        Task OnEventMarketDataPartialAsync(string data);

        /// <summary>1505-json-partial — Candle Data partial update.</summary>
        Task OnEventCandleDataPartialAsync(string data);

        /// <summary>1510-json-partial — Open Interest partial update.</summary>
        Task OnEventOpenInterestPartialAsync(string data);

        /// <summary>1512-json-partial — Last Traded Price partial update.</summary>
        Task OnEventLastTradedPricePartialAsync(string data);

        /// <summary>1105-json-partial — Instrument Change partial update.</summary>
        Task OnEventInstrumentChangePartialAsync(string data);
    }
}