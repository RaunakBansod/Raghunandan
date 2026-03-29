// using System.Threading.Tasks;

// /// <summary>
// /// Interface for Market Data Socket Client handlers
// /// Defines methods that should be implemented to handle various market data events
// /// </summary>
// public interface IMarketDataSocketClient
// {
//     /// <summary>
//     /// Called when connected to the socket
//     /// </summary>
//     Task OnConnectAsync();

//     /// <summary>
//     /// Called on receiving a message
//     /// </summary>
//     Task OnMessageAsync(string data);

//     /// <summary>
//     /// Called on receiving message code 1502: Market Data full
//     /// </summary>
//     Task OnEventMarketDataFullAsync(string data);

//     /// <summary>
//     /// Called on receiving message code 1507: Market Status full
//     /// </summary>
//     Task OnEventMarketStatusFullAsync(string data);

//     /// <summary>
//     /// Called on receiving message code 1512: LTP (Last Traded Price) full
//     /// </summary>
//     Task OnEventLastTradedPriceFullAsync(string data);

//     /// <summary>
//     /// Called on receiving message code 1505: Candle Data full
//     /// </summary>
//     Task OnEventCandleDataFullAsync(string data);

//     /// <summary>
//     /// Called on receiving message code 1510: OpenInterest full
//     /// </summary>
//     Task OnEventOpenInterestFullAsync(string data);

//     /// <summary>
//     /// Called on receiving message code 1501: Touchline full
//     /// </summary>
//     Task OnEventTouchlineFullAsync(string data);

//     /// <summary>
//     /// Called on receiving message code 1105: Instrument Change full
//     /// </summary>
//     Task OnEventInstrumentChangeFullAsync(string data);

//     /// <summary>
//     /// Called on receiving message code 1502: Market Data partial
//     /// </summary>
//     Task OnEventMarketDataPartialAsync(string data);

//     /// <summary>
//     /// Called on receiving message code 1512: LTP partial
//     /// </summary>
//     Task OnEventLastTradedPricePartialAsync(string data);

//     /// <summary>
//     /// Called on receiving message code 1505: Candle Data partial
//     /// </summary>
//     Task OnEventCandleDataPartialAsync(string data);

//     /// <summary>
//     /// Called on receiving message code 1510: OpenInterest partial
//     /// </summary>
//     Task OnEventOpenInterestPartialAsync(string data);

//     /// <summary>
//     /// Called on receiving message code 1501: Touchline partial
//     /// </summary>
//     Task OnEventTouchlinePartialAsync(string data);

//     /// <summary>
//     /// Called on receiving message code 1105: Instrument Change partial
//     /// </summary>
//     Task OnEventInstrumentChangePartialAsync(string data);

//     /// <summary>
//     /// Called when disconnected from the socket
//     /// </summary>
//     Task OnDisconnectAsync();

//     /// <summary>
//     /// Called when an error occurs on the socket
//     /// </summary>
//     Task OnErrorAsync(string data);
// }
