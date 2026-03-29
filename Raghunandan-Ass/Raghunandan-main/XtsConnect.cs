using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using DotNetEnv;

// Load the file
Env.Load();

namespace XtsApiClient
{
    /// <summary>
    /// Base variables class — holds session state after login.
    /// </summary>
    public class XtsCommon
    {
        public string? Token { get; protected set; }
        public string? UserID { get; protected set; }
        public bool? IsInvestorClient { get; protected set; }

        protected XtsCommon(string? token = null, string? userID = null, bool? isInvestorClient = null)
        {
            Token = token;
            UserID = userID;
            IsInvestorClient = isInvestorClient;
        }
    }

    /// <summary>
    /// The XTS Connect API wrapper class (async).
    /// </summary>
    public class XtsConnect : XtsCommon
    {
        // ── Products ─────────────────────────────────────────────────────────
        public const string ProductMis  = "MIS";
        public const string ProductNrml = "NRML";
        public const string ProductCnc  = "CNC";

        // ── Order Types ───────────────────────────────────────────────────────
        public const string OrderTypeMarket     = "MARKET";
        public const string OrderTypeLimit      = "LIMIT";
        public const string OrderTypeStopMarket = "STOPMARKET";
        public const string OrderTypeStopLimit  = "STOPLIMIT";

        // ── Transaction Types ─────────────────────────────────────────────────
        public const string TransactionTypeBuy  = "BUY";
        public const string TransactionTypeSell = "SELL";

        // ── Squareoff Modes ───────────────────────────────────────────────────
        public const string SquareoffDaywise = "DayWise";
        public const string SquareoffNetwise = "Netwise";

        // ── Squareoff Quantity Types ──────────────────────────────────────────
        public const string SquareoffQtyExact      = "ExactQty";
        public const string SquareoffQtyPercentage = "Percentage";

        // ── Time-in-Force ─────────────────────────────────────────────────────
        public const string TifGtc        = "GTC";
        public const string TifIoc        = "IOC";
        public const string TifFok        = "FOK";
        public const string TifGtd        = "GTD";
        public const string TifDay        = "DAY";
        public const string TifAtTheOpen  = "AT_THE_OPEN";
        public const string TifAtTheClose = "AT_THE_CLOSE";

        // ── Exchange Segments ─────────────────────────────────────────────────
        public const string ExchangeNsecm = "NSECM";
        public const string ExchangeNsefo = "NSEFO";
        public const string ExchangeNsecd = "NSECD";
        public const string ExchangeMcxfo = "MCXFO";
        public const string ExchangeBsecm = "BSECM";
        public const string ExchangeBsefo = "BSEFO";

        // ── API Route Map ─────────────────────────────────────────────────────
        private static readonly Dictionary<string, string> Routes = new()
        {
            // Interactive
            ["interactive.prefix"]          = "interactive",
            ["user.login"]                  = "/interactive/user/session",
            ["user.logout"]                 = "/interactive/user/session",
            ["user.profile"]                = "/interactive/user/profile",
            ["user.balance"]                = "/interactive/user/balance",
            ["orders"]                      = "/interactive/orders",
            ["trades"]                      = "/interactive/orders/trades",
            ["order.status"]                = "/interactive/orders",
            ["order.place"]                 = "/interactive/orders",
            ["bracketorder.place"]          = "/interactive/orders/bracket",
            ["bracketorder.modify"]         = "/interactive/orders/bracket",
            ["bracketorder.cancel"]         = "/interactive/orders/bracket",
            ["order.place.cover"]           = "/interactive/orders/cover",
            ["order.exit.cover"]            = "/interactive/orders/cover",
            ["order.modify"]                = "/interactive/orders",
            ["order.cancel"]                = "/interactive/orders",
            ["order.cancelall"]             = "/interactive/orders/cancelall",
            ["order.history"]               = "/interactive/orders",
            ["portfolio.positions"]         = "/interactive/portfolio/positions",
            ["portfolio.holdings"]          = "/interactive/portfolio/holdings",
            ["portfolio.positions.convert"] = "/interactive/portfolio/positions/convert",
            ["portfolio.squareoff"]         = "/interactive/portfolio/squareoff",
            ["portfolio.dealerpositions"]   = "interactive/portfolio/dealerpositions",
            ["order.dealer.status"]         = "/interactive/orders/dealerorderbook",
            ["dealer.trades"]               = "/interactive/orders/dealertradebook",

            // Market Data
            ["marketdata.prefix"]                                   = "apimarketdata",
            ["market.login"]                                        = "/apimarketdata/auth/login",
            ["market.logout"]                                       = "/apimarketdata/auth/logout",
            ["market.config"]                                       = "/apimarketdata/config/clientConfig",
            ["market.instruments.master"]                           = "/apimarketdata/instruments/master",
            ["market.instruments.subscription"]                     = "/apimarketdata/instruments/subscription",
            ["market.instruments.unsubscription"]                   = "/apimarketdata/instruments/subscription",
            ["market.instruments.ohlc"]                             = "/apimarketdata/instruments/ohlc",
            ["market.instruments.indexlist"]                        = "/apimarketdata/instruments/indexlist",
            ["market.instruments.quotes"]                           = "/apimarketdata/instruments/quotes",
            ["market.search.instrumentsbyid"]                       = "/apimarketdata/search/instrumentsbyid",
            ["market.search.instrumentsbystring"]                   = "/apimarketdata/search/instruments",
            ["market.instruments.instrument.series"]                = "/apimarketdata/instruments/instrument/series",
            ["market.instruments.instrument.equitysymbol"]         = "/apimarketdata/instruments/instrument/symbol",
            ["market.instruments.instrument.futuresymbol"]         = "/apimarketdata/instruments/instrument/futureSymbol",
            ["market.instruments.instrument.optionsymbol"]         = "/apimarketdata/instruments/instrument/optionsymbol",
            ["market.instruments.instrument.optiontype"]           = "/apimarketdata/instruments/instrument/optionType",
            ["market.instruments.instrument.expirydate"]           = "/apimarketdata/instruments/instrument/expiryDate",
        };

        // ── Private fields ────────────────────────────────────────────────────
        string apiKey = Environment.GetEnvironmentVariable("API_KEY");

        private readonly ILogger<XtsConnect> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _root;
        private readonly string _apiKey;
        private readonly string _secretKey;
        private readonly string _source;
        private readonly bool _debug;
        private DateTimeOffset? _lastLoginTime;

        /// <summary>
        /// Initialise a new XTS Connect client instance.
        /// </summary>
        /// <param name="apiKey">API key issued to you.</param>
        /// <param name="secretKey">Secret key issued to you.</param>
        /// <param name="source">Source identifier.</param>
        /// <param name="root">API root URL.</param>
        /// <param name="debug">If true, logs all requests/responses.</param>
        /// <param name="timeoutSeconds">Request timeout in seconds (default 1200).</param>
        /// <param name="disableSsl">Disable SSL certificate validation.</param>
        /// <param name="logger">Optional logger instance.</param>
        public XtsConnect(
            string apiKey,
            string secretKey,
            string source,
            string root,
            bool debug = false,
            int timeoutSeconds = 1200,
            bool disableSsl = true,
            ILogger<XtsConnect>? logger = null)
            : base()
        {
            _apiKey    = apiKey;
            _secretKey = secretKey;
            _source    = source;
            _root      = root.TrimEnd('/');
            _debug     = debug;
            _logger    = logger ?? LoggerFactory.Create(b => b.AddConsole()).CreateLogger<XtsConnect>();

            var handler = new HttpClientHandler();
            if (disableSsl)
                handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };
        }

        // ────────────────────────────────────────────────────────────────────
        // Internal helpers
        // ────────────────────────────────────────────────────────────────────

        private void SetCommonVariables(string accessToken, string userID, bool isInvestorClient)
        {
            Token           = accessToken;
            UserID          = userID;
            IsInvestorClient = isInvestorClient;
        }

        /// <summary>
        /// Centralised response handler. Returns the response dictionary on success,
        /// throws on error.
        /// </summary>
        private Dictionary<string, object?> HandleResponse(Dictionary<string, object?> response, string operation)
        {
            if (response.TryGetValue("type", out var type))
            {
                if (type?.ToString() == "success")
                    return response;

                if (type?.ToString() == "error")
                {
                    var msg = $"{operation} failed: {response.GetValueOrDefault("description", "Unknown error")}";
                    _logger.LogError(msg);
                    throw new Exception(msg);
                }
            }

            // Handle inconsistent XTS API behaviour — result present but no type
            if (response.ContainsKey("result"))
                return response;

            var fallback = $"{operation} failed: Unexpected response format";
            _logger.LogError(fallback);
            throw new Exception(fallback);
        }

        /// <summary>
        /// Adds clientID to the params dict based on IsInvestorClient flag.
        /// </summary>
        private Dictionary<string, object?> AddClientId(Dictionary<string, object?> p, string clientID = "*****")
        {
            p["clientID"] = (IsInvestorClient == true)
                ? (string.IsNullOrEmpty(clientID) ? UserID : clientID)
                : "*****";
            return p;
        }

        // ────────────────────────────────────────────────────────────────────
        // Low-level HTTP verbs
        // ────────────────────────────────────────────────────────────────────

        private Task<Dictionary<string, object?>> GetAsync(string route, Dictionary<string, object?>? p = null)
            => RequestAsync(route, HttpMethod.Get, p);

        private Task<Dictionary<string, object?>> PostAsync(string route, object? body = null)
            => RequestAsync(route, HttpMethod.Post, body);

        private Task<Dictionary<string, object?>> PutAsync(string route, object? body = null)
            => RequestAsync(route, HttpMethod.Put, body);

        private Task<Dictionary<string, object?>> DeleteAsync(string route, Dictionary<string, object?>? p = null)
            => RequestAsync(route, HttpMethod.Delete, p);

        /// <summary>
        /// Core HTTP request dispatcher.
        /// </summary>
        private async Task<Dictionary<string, object?>> RequestAsync(
            string route, HttpMethod method, object? parameters = null)
        {
            var uri = Routes[route];
            var url = _root + uri;

            var request = new HttpRequestMessage(method, url);

            if (!string.IsNullOrEmpty(Token))
            {
                request.Headers.Add("Authorization", Token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }

            // Body vs query-string
            bool hasBody = method == HttpMethod.Post || method == HttpMethod.Put;
            if (hasBody && parameters is not null)
            {
                string body = parameters is string s ? s : JsonSerializer.Serialize(parameters);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }
            else if (!hasBody && parameters is Dictionary<string, object?> queryParams)
            {
                var qs = BuildQueryString(queryParams);
                if (!string.IsNullOrEmpty(qs))
                    request.RequestUri = new Uri(url + "?" + qs);
            }

            HttpResponseMessage r;
            try
            {
                r = await _httpClient.SendAsync(request);
            }
            catch (Exception e)
            {
                _logger.LogError("Request failed for {Method} {Url}: {Error}", method, url, e.Message);
                throw;
            }

            if (_debug)
                _logger.LogDebug("Response: {Code}", r.StatusCode);

            var contentType = r.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!contentType.Contains("json"))
            {
                throw new XtsDataException(
                    $"Unknown Content-Type ({contentType}) with response from {url}");
            }

            var raw = await r.Content.ReadAsStringAsync();
            Dictionary<string, object?> data;
            try
            {
                data = JsonSerializer.Deserialize<Dictionary<string, object?>>(raw)
                       ?? throw new XtsDataException("Empty JSON response.");
            }
            catch (JsonException)
            {
                _logger.LogError("JSON parsing failed for response: {Content}", raw);
                throw new XtsDataException($"Couldn't parse the JSON response: {raw}");
            }

            // Specific API error checks
            if (data.TryGetValue("type", out var t) && t?.ToString() == "error")
            {
                var desc = data.GetValueOrDefault("description")?.ToString() ?? "";
                if ((int)r.StatusCode == 400 && desc == "Invalid Token")
                    throw new XtsTokenException(desc);

                if ((int)r.StatusCode == 400 && desc == "Bad Request")
                {
                    var errors = (data.GetValueOrDefault("result") as Dictionary<string, object?>)
                                 ?.GetValueOrDefault("errors") ?? "[]";
                    throw new XtsInputException($"Description: {desc} errors: {errors}");
                }
            }

            return data;
        }

        /// <summary>
        /// Builds a URL-encoded query string from a dictionary.
        /// </summary>
        private static string BuildQueryString(Dictionary<string, object?> p)
        {
            var parts = new List<string>();
            foreach (var (k, v) in p)
                if (v is not null)
                    parts.Add($"{Uri.EscapeDataString(k)}={Uri.EscapeDataString(v.ToString()!)}");
            return string.Join("&", parts);
        }

        // ────────────────────────────────────────────────────────────────────
        // Interactive API — Authentication
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Logs in using the interactive API and stores the session token.
        /// </summary>
        public async Task<Dictionary<string, object?>> InteractiveLoginAsync()
        {
            var p = new Dictionary<string, object?>
            {
                ["appKey"]    = _apiKey,
                ["secretKey"] = _secretKey,
                ["source"]    = _source,
            };
            var response = await PostAsync("user.login", p);

            if (response.GetValueOrDefault("type")?.ToString() == "success")
            {
                var result = (JsonElement)response["result"]!;
                SetCommonVariables(
                    result.GetProperty("token").GetString()!,
                    result.GetProperty("userID").GetString()!,
                    result.GetProperty("isInvestorClient").GetBoolean());
                _lastLoginTime = DateTimeOffset.UtcNow;
                return response;
            }
            else if (response.GetValueOrDefault("type")?.ToString() == "error")
            {
                var msg = $"Login failed: {response.GetValueOrDefault("description", "Unknown error")}";
                _logger.LogError(msg);
                throw new Exception(msg);
            }
            throw new Exception("Login failed: Unexpected response format");
        }

        /// <summary>
        /// Logs out and invalidates the session token.
        /// </summary>
        public async Task<Dictionary<string, object?>> InteractiveLogoutAsync(string clientID = "*****")
        {
            var p = new Dictionary<string, object?>();
            AddClientId(p, clientID);
            var response = await DeleteAsync("user.logout", p);
            Token = null;
            return HandleResponse(response, "Interactive Logout");
        }

        // ────────────────────────────────────────────────────────────────────
        // Interactive API — Account
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Get a user profile details.</summary>
        public async Task<Dictionary<string, object?>> GetProfileAsync(string clientID = "*****")
        {
            var p = new Dictionary<string, object?>();
            AddClientId(p, clientID);
            return HandleResponse(await GetAsync("user.profile", p), "Get Profile");
        }

        /// <summary>Get balance / margin details for the user.</summary>
        public async Task<Dictionary<string, object?>> GetBalanceAsync(string clientID = "*****")
        {
            var p = new Dictionary<string, object?>();
            AddClientId(p, clientID);
            return HandleResponse(await GetAsync("user.balance", p), "Get Balance");
        }

        // ────────────────────────────────────────────────────────────────────
        // Interactive API — Orders
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Get the order book for the current session.</summary>
        public async Task<Dictionary<string, object?>> GetOrderBookAsync(string clientID = "*****")
        {
            var p = new Dictionary<string, object?>();
            AddClientId(p, clientID);
            return HandleResponse(await GetAsync("order.status", p), "Get Order Book");
        }

        /// <summary>Get the dealer order book.</summary>
        public async Task<Dictionary<string, object?>> GetDealerOrderBookAsync(string clientID = "*****")
        {
            var p = new Dictionary<string, object?>();
            AddClientId(p, clientID);
            return HandleResponse(await GetAsync("order.dealer.status", p), "Get Dealer Order Book");
        }

        /// <summary>Place a regular order.</summary>
        public async Task<Dictionary<string, object?>> PlaceOrderAsync(
            string exchangeSegment,
            long   exchangeInstrumentID,
            string productType,
            string orderType,
            string orderSide,
            string timeInForce,
            int    disclosedQuantity,
            int    orderQuantity,
            double limitPrice,
            double stopPrice,
            string orderUniqueIdentifier,
            string clientID = "*****")
        {
            var p = new Dictionary<string, object?>
            {
                ["exchangeSegment"]       = exchangeSegment,
                ["exchangeInstrumentID"]  = exchangeInstrumentID,
                ["productType"]           = productType,
                ["orderType"]             = orderType,
                ["orderSide"]             = orderSide,
                ["timeInForce"]           = timeInForce,
                ["disclosedQuantity"]     = disclosedQuantity,
                ["orderQuantity"]         = orderQuantity,
                ["limitPrice"]            = limitPrice,
                ["stopPrice"]             = stopPrice,
                ["orderUniqueIdentifier"] = orderUniqueIdentifier,
            };
            AddClientId(p, clientID);
            return HandleResponse(await PostAsync("order.place", p), "Place Order");
        }

        /// <summary>Place a bracket order.</summary>
        public async Task<Dictionary<string, object?>> PlaceBracketOrderAsync(
            string exchangeSegment,
            long   exchangeInstrumentID,
            string orderType,
            string orderSide,
            int    disclosedQuantity,
            int    orderQuantity,
            double limitPrice,
            double squarOff,
            double stopLossPrice,
            double trailingStoploss,
            bool   isProOrder,
            string orderUniqueIdentifier,
            string clientID = "*****")
        {
            var p = new Dictionary<string, object?>
            {
                ["exchangeSegment"]       = exchangeSegment,
                ["exchangeInstrumentID"]  = exchangeInstrumentID,
                ["orderType"]             = orderType,
                ["orderSide"]             = orderSide,
                ["disclosedQuantity"]     = disclosedQuantity,
                ["orderQuantity"]         = orderQuantity,
                ["limitPrice"]            = limitPrice,
                ["squarOff"]              = squarOff,
                ["stopLossPrice"]         = stopLossPrice,
                ["trailingStoploss"]      = trailingStoploss,
                ["isProOrder"]            = isProOrder,
                ["orderUniqueIdentifier"] = orderUniqueIdentifier,
            };
            AddClientId(p, clientID);
            return HandleResponse(await PostAsync("bracketorder.place", p), "Place Bracket Order");
        }

        /// <summary>Modify an open order.</summary>
        public async Task<Dictionary<string, object?>> ModifyOrderAsync(
            long   appOrderID,
            string modifiedProductType,
            string modifiedOrderType,
            int    modifiedOrderQuantity,
            int    modifiedDisclosedQuantity,
            double modifiedLimitPrice,
            double modifiedStopPrice,
            string modifiedTimeInForce,
            string orderUniqueIdentifier,
            string clientID = "*****")
        {
            var p = new Dictionary<string, object?>
            {
                ["appOrderID"]                = appOrderID,
                ["modifiedProductType"]       = modifiedProductType,
                ["modifiedOrderType"]         = modifiedOrderType,
                ["modifiedOrderQuantity"]     = modifiedOrderQuantity,
                ["modifiedDisclosedQuantity"] = modifiedDisclosedQuantity,
                ["modifiedLimitPrice"]        = modifiedLimitPrice,
                ["modifiedStopPrice"]         = modifiedStopPrice,
                ["modifiedTimeInForce"]       = modifiedTimeInForce,
                ["orderUniqueIdentifier"]     = orderUniqueIdentifier,
            };
            AddClientId(p, clientID);
            return HandleResponse(await PutAsync("order.modify", p), "Modify Order");
        }

        /// <summary>Cancel an open order.</summary>
        public async Task<Dictionary<string, object?>> CancelOrderAsync(
            long   appOrderID,
            string orderUniqueIdentifier,
            string clientID = "*****")
        {
            var p = new Dictionary<string, object?>
            {
                ["appOrderID"]            = appOrderID,
                ["orderUniqueIdentifier"] = orderUniqueIdentifier,
            };
            AddClientId(p, clientID);
            return HandleResponse(await DeleteAsync("order.cancel", p), "Cancel Order");
        }

        /// <summary>Cancel all open orders for an instrument.</summary>
        public async Task<Dictionary<string, object?>> CancelAllOrderAsync(
            string exchangeSegment,
            long   exchangeInstrumentID,
            string clientID = "*****")
        {
            var p = new Dictionary<string, object?>
            {
                ["exchangeSegment"]      = exchangeSegment,
                ["exchangeInstrumentID"] = exchangeInstrumentID,
            };
            AddClientId(p, clientID);
            return HandleResponse(await PostAsync("order.cancelall", p), "Cancel All Order");
        }

        /// <summary>Get order history trail for a specific order.</summary>
        public async Task<Dictionary<string, object?>> GetOrderHistoryAsync(long appOrderID, string clientID = "*****")
        {
            var p = new Dictionary<string, object?> { ["appOrderID"] = appOrderID };
            AddClientId(p, clientID);
            return HandleResponse(await GetAsync("order.history", p), "Get Order History");
        }

        // ────────────────────────────────────────────────────────────────────
        // Interactive API — Cover Orders
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Place a cover order.</summary>
        public async Task<Dictionary<string, object?>> PlaceCoverOrderAsync(
            string exchangeSegment,
            long   exchangeInstrumentID,
            string orderSide,
            string orderType,
            int    orderQuantity,
            int    disclosedQuantity,
            double limitPrice,
            double stopPrice,
            string orderUniqueIdentifier,
            string clientID = "*****")
        {
            var p = new Dictionary<string, object?>
            {
                ["exchangeSegment"]       = exchangeSegment,
                ["exchangeInstrumentID"]  = exchangeInstrumentID,
                ["orderSide"]             = orderSide,
                ["orderType"]             = orderType,
                ["orderQuantity"]         = orderQuantity,
                ["disclosedQuantity"]     = disclosedQuantity,
                ["limitPrice"]            = limitPrice,
                ["stopPrice"]             = stopPrice,
                ["orderUniqueIdentifier"] = orderUniqueIdentifier,
            };
            AddClientId(p, clientID);
            return HandleResponse(await PostAsync("order.place.cover", p), "Place Cover Order");
        }

        /// <summary>Exit an open cover order.</summary>
        public async Task<Dictionary<string, object?>> ExitCoverOrderAsync(long appOrderID, string clientID = "*****")
        {
            var p = new Dictionary<string, object?> { ["appOrderID"] = appOrderID };
            AddClientId(p, clientID);
            return HandleResponse(await PutAsync("order.exit.cover", p), "Exit Cover Order");
        }

        // ────────────────────────────────────────────────────────────────────
        // Interactive API — Bracket Orders
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Cancel a bracket order by entry order ID.</summary>
        public async Task<Dictionary<string, object?>> BracketOrderCancelAsync(long appOrderID, string clientID = "*****")
        {
            var p = new Dictionary<string, object?> { ["boEntryOrderId"] = appOrderID };
            AddClientId(p, clientID);
            return HandleResponse(await DeleteAsync("bracketorder.cancel", p), "Cancel Bracket Order");
        }

        // ────────────────────────────────────────────────────────────────────
        // Interactive API — Trades & Holdings
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Get the trade book for the day.</summary>
        public async Task<Dictionary<string, object?>> GetTradeAsync(string clientID = "*****")
        {
            var p = new Dictionary<string, object?>();
            AddClientId(p, clientID);
            return HandleResponse(await GetAsync("trades", p), "Get Trade");
        }

        /// <summary>Get the dealer trade book.</summary>
        public async Task<Dictionary<string, object?>> GetDealerTradebookAsync(string clientID = "*****")
        {
            var p = new Dictionary<string, object?>();
            AddClientId(p, clientID);
            return HandleResponse(await GetAsync("dealer.trades", p), "Get Dealer Tradebook");
        }

        /// <summary>Get long-term holdings.</summary>
        public async Task<Dictionary<string, object?>> GetHoldingAsync(string clientID = "*****")
        {
            var p = new Dictionary<string, object?>();
            AddClientId(p, clientID);
            return HandleResponse(await GetAsync("portfolio.holdings", p), "Get Holding");
        }

        // ────────────────────────────────────────────────────────────────────
        // Interactive API — Positions
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Day-wise positions.</summary>
        public async Task<Dictionary<string, object?>> GetPositionDaywiseAsync(string clientID = "*****")
        {
            var p = new Dictionary<string, object?> { ["dayOrNet"] = "DayWise" };
            AddClientId(p, clientID);
            return HandleResponse(await GetAsync("portfolio.positions", p), "Get Position Daywise");
        }

        /// <summary>Net positions.</summary>
        public async Task<Dictionary<string, object?>> GetPositionNetwiseAsync(string clientID = "*****")
        {
            var p = new Dictionary<string, object?> { ["dayOrNet"] = "NetWise" };
            AddClientId(p, clientID);
            return HandleResponse(await GetAsync("portfolio.positions", p), "Get Position Netwise");
        }

        /// <summary>Dealer day-wise positions.</summary>
        public async Task<Dictionary<string, object?>> GetDealerPositionDaywiseAsync(string clientID = "*****")
        {
            var p = new Dictionary<string, object?> { ["dayOrNet"] = "DayWise" };
            AddClientId(p, clientID);
            return HandleResponse(await GetAsync("portfolio.dealerpositions", p), "Get Dealer Position Daywise");
        }

        /// <summary>Dealer net positions.</summary>
        public async Task<Dictionary<string, object?>> GetDealerPositionNetwiseAsync(string clientID = "*****")
        {
            var p = new Dictionary<string, object?> { ["dayOrNet"] = "NetWise" };
            AddClientId(p, clientID);
            return HandleResponse(await GetAsync("portfolio.dealerpositions", p), "Get Dealer Position Netwise");
        }

        /// <summary>Convert a position from one product type to another.</summary>
        public async Task<Dictionary<string, object?>> ConvertPositionAsync(
            string exchangeSegment,
            long   exchangeInstrumentID,
            int    targetQty,
            bool   isDayWise,
            string oldProductType,
            string newProductType,
            string clientID = "*****")
        {
            var p = new Dictionary<string, object?>
            {
                ["exchangeSegment"]      = exchangeSegment,
                ["exchangeInstrumentID"] = exchangeInstrumentID,
                ["targetQty"]            = targetQty,
                ["isDayWise"]            = isDayWise,
                ["oldProductType"]       = oldProductType,
                ["newProductType"]       = newProductType,
            };
            AddClientId(p, clientID);
            return HandleResponse(await PutAsync("portfolio.positions.convert", p), "Convert Position");
        }

        /// <summary>Square off one or more positions.</summary>
        public async Task<Dictionary<string, object?>> SquareoffPositionAsync(
            string exchangeSegment,
            long   exchangeInstrumentID,
            string productType,
            string squareoffMode,
            string positionSquareOffQuantityType,
            double squareOffQtyValue,
            bool   blockOrderSending,
            bool   cancelOrders,
            string clientID = "*****")
        {
            var p = new Dictionary<string, object?>
            {
                ["exchangeSegment"]              = exchangeSegment,
                ["exchangeInstrumentID"]         = exchangeInstrumentID,
                ["productType"]                  = productType,
                ["squareoffMode"]                = squareoffMode,
                ["positionSquareOffQuantityType"]= positionSquareOffQuantityType,
                ["squareOffQtyValue"]            = squareOffQtyValue,
                ["blockOrderSending"]            = blockOrderSending,
                ["cancelOrders"]                 = cancelOrders,
            };
            AddClientId(p, clientID);
            return HandleResponse(await PutAsync("portfolio.squareoff", p), "Squareoff Position");
        }

        // ────────────────────────────────────────────────────────────────────
        // Interactive API — Resilient V2 Methods (with retry logic)
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Place order with timeout/connect-error retry logic (V2).
        /// </summary>
        public async Task<Dictionary<string, object?>> PlaceOrderV2Async(
            string exchangeSegment,
            long   exchangeInstrumentID,
            string productType,
            string orderType,
            string orderSide,
            string timeInForce,
            int    disclosedQuantity,
            int    orderQuantity,
            double limitPrice,
            double stopPrice,
            string orderUniqueIdentifier,
            int    timeoutMaxRetries        = 4,
            double timeoutRetryDelay        = 0.5,
            int    connectErrorMaxRetries   = 2,
            double connectErrorRetryDelay   = 3,
            string clientID = "*****")
        {
            var p = new Dictionary<string, object?>
            {
                ["exchangeSegment"]       = exchangeSegment,
                ["exchangeInstrumentID"]  = exchangeInstrumentID,
                ["productType"]           = productType,
                ["orderType"]             = orderType,
                ["orderSide"]             = orderSide,
                ["timeInForce"]           = timeInForce,
                ["disclosedQuantity"]     = disclosedQuantity,
                ["orderQuantity"]         = orderQuantity,
                ["limitPrice"]            = limitPrice,
                ["stopPrice"]             = stopPrice,
                ["orderUniqueIdentifier"] = orderUniqueIdentifier,
                ["clientID"]              = (IsInvestorClient == true) ? UserID : "*****",
            };

            return await ExecuteWithRetryAsync(
                () => PostAsync("order.place", p),
                $"InstrumentID={exchangeInstrumentID} Qty={orderQuantity}",
                "placing order",
                timeoutMaxRetries, timeoutRetryDelay,
                connectErrorMaxRetries, connectErrorRetryDelay);
        }

        /// <summary>
        /// Cancel order with timeout/connect-error retry logic (V2).
        /// </summary>
        public async Task<Dictionary<string, object?>> CancelOrderV2Async(
            long   appOrderID,
            string orderUniqueIdentifier,
            int    timeoutMaxRetries        = 4,
            double timeoutRetryDelay        = 0.5,
            int    connectErrorMaxRetries   = 2,
            double connectErrorRetryDelay   = 3,
            string clientID = "*****")
        {
            var p = new Dictionary<string, object?>
            {
                ["appOrderID"]            = appOrderID,
                ["orderUniqueIdentifier"] = orderUniqueIdentifier,
                ["clientID"]              = (IsInvestorClient == true) ? UserID : "*****",
            };

            return await ExecuteWithRetryAsync(
                () => DeleteAsync("order.cancel", p),
                $"appOrderID={appOrderID}",
                "cancelling order",
                timeoutMaxRetries, timeoutRetryDelay,
                connectErrorMaxRetries, connectErrorRetryDelay);
        }

        /// <summary>
        /// Modify order with timeout/connect-error retry logic (V2).
        /// </summary>
        public async Task<Dictionary<string, object?>> ModifyOrderV2Async(
            long   appOrderID,
            string modifiedProductType,
            string modifiedOrderType,
            int    modifiedOrderQuantity,
            int    modifiedDisclosedQuantity,
            double modifiedLimitPrice,
            double modifiedStopPrice,
            string modifiedTimeInForce,
            string orderUniqueIdentifier,
            int    timeoutMaxRetries        = 4,
            double timeoutRetryDelay        = 0.5,
            int    connectErrorMaxRetries   = 2,
            double connectErrorRetryDelay   = 3,
            string clientID = "*****")
        {
            var p = new Dictionary<string, object?>
            {
                ["appOrderID"]                = appOrderID,
                ["modifiedProductType"]       = modifiedProductType,
                ["modifiedOrderType"]         = modifiedOrderType,
                ["modifiedOrderQuantity"]     = modifiedOrderQuantity,
                ["modifiedDisclosedQuantity"] = modifiedDisclosedQuantity,
                ["modifiedLimitPrice"]        = modifiedLimitPrice,
                ["modifiedStopPrice"]         = modifiedStopPrice,
                ["modifiedTimeInForce"]       = modifiedTimeInForce,
                ["orderUniqueIdentifier"]     = orderUniqueIdentifier,
                ["clientID"]                  = (IsInvestorClient == true) ? UserID : "*****",
            };

            return await ExecuteWithRetryAsync(
                () => PutAsync("order.modify", p),
                $"AppOrderID={appOrderID}",
                "modifying order",
                timeoutMaxRetries, timeoutRetryDelay,
                connectErrorMaxRetries, connectErrorRetryDelay);
        }

        /// <summary>
        /// Cancel all orders with timeout/connect-error retry logic (V2).
        /// </summary>
        public async Task<Dictionary<string, object?>> CancelAllOrderV2Async(
            string exchangeSegment,
            long   exchangeInstrumentID,
            int    timeoutMaxRetries        = 4,
            double timeoutRetryDelay        = 0.5,
            int    connectErrorMaxRetries   = 2,
            double connectErrorRetryDelay   = 3,
            string clientID = "*****")
        {
            var p = new Dictionary<string, object?>
            {
                ["exchangeSegment"]      = exchangeSegment,
                ["exchangeInstrumentID"] = exchangeInstrumentID,
                ["clientID"]             = (IsInvestorClient == true) ? UserID : "*****",
            };

            return await ExecuteWithRetryAsync(
                () => PostAsync("order.cancelall", p),
                $"exchangeSegment={exchangeSegment} exchangeInstrumentID={exchangeInstrumentID}",
                "cancelling all orders",
                timeoutMaxRetries, timeoutRetryDelay,
                connectErrorMaxRetries, connectErrorRetryDelay);
        }

        /// <summary>
        /// Shared retry engine used by all V2 order methods.
        /// Mirrors the Python pattern: outer attempt → timeout retry loop →
        /// HttpRequestException (connect) retry loop → general exception.
        /// </summary>
        private async Task<Dictionary<string, object?>> ExecuteWithRetryAsync(
            Func<Task<Dictionary<string, object?>>> action,
            string context,
            string operationName,
            int    timeoutMaxRetries,
            double timeoutRetryDelay,
            int    connectErrorMaxRetries,
            double connectErrorRetryDelay)
        {
            Dictionary<string, object?>? response = null;

            // ── OUTER FIRST ATTEMPT ───────────────────────────────────────────
            try
            {
                response = await action();
                return response;
            }
            catch (TaskCanceledException timeoutEx) when (timeoutEx.InnerException is TimeoutException)
            {
                // ── TIMEOUT RETRY LOOP ────────────────────────────────────────
                for (int attempt = 0; attempt < timeoutMaxRetries; attempt++)
                {
                    Dictionary<string, object?>? inner = null;
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(timeoutRetryDelay));
                        inner = await action();
                        return inner;
                    }
                    catch (TaskCanceledException te) when (te.InnerException is TimeoutException)
                    {
                        if (attempt == timeoutMaxRetries - 1)
                            throw new XtsNetworkException(
                                $"[Timeout] Failed after {timeoutMaxRetries} retries | {context} | Error={te.Message} | Response={inner}");
                    }
                    catch (Exception ie)
                    {
                        throw new XtsInputException(
                            $"[Unknown Error] {operationName} | {context} | Error={ie.Message} | Response={inner}");
                    }
                }
            }
            catch (HttpRequestException connectEx)
            {
                // ── CONNECT ERROR RETRY LOOP ──────────────────────────────────
                Console.WriteLine($"ConnectError caught, entering retry loop... {context}");
                for (int attempt = 0; attempt < connectErrorMaxRetries; attempt++)
                {
                    Dictionary<string, object?>? inner = null;
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(connectErrorRetryDelay));
                        inner = await action();
                        return inner;
                    }
                    catch (HttpRequestException ie)
                    {
                        Console.WriteLine($"ConnectError retry {attempt + 1} failed. {context}");
                        if (attempt == connectErrorMaxRetries - 1)
                            throw new XtsNetworkException(
                                $"[ConnectError] Failed after {connectErrorMaxRetries} retries | {context} | Error={ie.Message} | Response={inner}");
                    }
                    catch (Exception ie)
                    {
                        Console.WriteLine($"Non-ConnectError exception in retry loop. {context}");
                        throw new XtsInputException(
                            $"[Unknown Error] {operationName} | {context} | Error={ie.Message} | Response={inner}");
                    }
                }
            }
            catch (Exception e)
            {
                // ── ALL OTHER ERRORS ──────────────────────────────────────────
                Console.WriteLine($"General exception caught in outer attempt. {context}");
                throw new XtsInputException(
                    $"[Unknown Error] {operationName} | {context} | Error={e.Message} | Response={response}");
            }

            // Unreachable, but satisfies the compiler
            throw new XtsNetworkException($"Unexpected exit from retry logic for {operationName} | {context}");
        }

        // ────────────────────────────────────────────────────────────────────
        // Market Data API — Authentication
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Log in to the market data API.</summary>
        public async Task<Dictionary<string, object?>> MarketdataLoginAsync()
        {
            var p = new Dictionary<string, object?>
            {
                ["appKey"]    = _apiKey,
                ["secretKey"] = _secretKey,
                ["source"]    = _source,
            };
            var response = await PostAsync("market.login", p);

            if (response.GetValueOrDefault("type")?.ToString() == "success")
            {
                var result = (JsonElement)response["result"]!;
                SetCommonVariables(
                    result.GetProperty("token").GetString()!,
                    result.GetProperty("userID").GetString()!,
                    false);
                _lastLoginTime = DateTimeOffset.UtcNow;
                return response;
            }
            else if (response.GetValueOrDefault("type")?.ToString() == "error")
            {
                var msg = $"API responded with error: {response.GetValueOrDefault("description", "Unknown error")}";
                _logger.LogError(msg);
                throw new Exception(msg);
            }
            throw new Exception("Unexpected API response format.");
        }

        /// <summary>Log out from the market data API.</summary>
        public async Task<Dictionary<string, object?>> MarketdataLogoutAsync()
        {
            var response = await DeleteAsync("market.logout");
            Token = null;
            return HandleResponse(response, "Market Data Logout");
        }

        // ────────────────────────────────────────────────────────────────────
        // Market Data API — Configuration & Quotes
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Get client configuration.</summary>
        public async Task<Dictionary<string, object?>> GetConfigAsync()
            => HandleResponse(await GetAsync("market.config"), "Get Config");

        /// <summary>Get live quote for instruments.</summary>
        public async Task<Dictionary<string, object?>> GetQuoteAsync(
            object instruments, int xtsMessageCode, string publishFormat)
        {
            var p = new Dictionary<string, object?>
            {
                ["instruments"]    = instruments,
                ["xtsMessageCode"] = xtsMessageCode,
                ["publishFormat"]  = publishFormat,
            };
            return HandleResponse(await PostAsync("market.instruments.quotes", p), "Get Quote");
        }

        // ────────────────────────────────────────────────────────────────────
        // Market Data API — Subscriptions
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Subscribe to live market data for a list of instruments.
        /// xtsMessageCode examples: 1501=Touchline, 1502=MarketDepth, 1512=LTP.
        /// </summary>
        public async Task<Dictionary<string, object?>> SendSubscriptionAsync(
            List<Dictionary<string, int>> instruments, int xtsMessageCode)
        {
            var p = new Dictionary<string, object?>
            {
                ["instruments"]    = instruments,
                ["xtsMessageCode"] = xtsMessageCode,
            };
            return HandleResponse(await PostAsync("market.instruments.subscription", p), "Send Subscription");
        }

        /// <summary>Unsubscribe from market data for a list of instruments.</summary>
        public async Task<Dictionary<string, object?>> SendUnsubscriptionAsync(
            List<Dictionary<string, int>> instruments, int xtsMessageCode)
        {
            var p = new Dictionary<string, object?>
            {
                ["instruments"]    = instruments,
                ["xtsMessageCode"] = xtsMessageCode,
            };
            return HandleResponse(await PutAsync("market.instruments.unsubscription", p), "Send Unsubscription");
        }

        // ────────────────────────────────────────────────────────────────────
        // Market Data API — Instruments
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Download instrument master for given exchange segments.</summary>
        public async Task<Dictionary<string, object?>> GetMasterAsync(List<int> exchangeSegmentList)
        {
            var p = new Dictionary<string, object?> { ["exchangeSegmentList"] = exchangeSegmentList };
            return HandleResponse(await PostAsync("market.instruments.master", p), "Get Master");
        }

        /// <summary>Retrieve historical OHLC candle data.</summary>
        public async Task<Dictionary<string, object?>> GetOhlcAsync(
            int    exchangeSegment,
            long   exchangeInstrumentID,
            string startTime,
            string endTime,
            int    compressionValue)
        {
            var p = new Dictionary<string, object?>
            {
                ["exchangeSegment"]      = exchangeSegment,
                ["exchangeInstrumentID"] = exchangeInstrumentID,
                ["startTime"]            = startTime,
                ["endTime"]              = endTime,
                ["compressionValue"]     = compressionValue,
            };
            return HandleResponse(await GetAsync("market.instruments.ohlc", p), "Get OHLC");
        }

        /// <summary>Get the index list for an exchange segment.</summary>
        public async Task<Dictionary<string, object?>> GetIndexListAsync(int exchangeSegment)
        {
            var p = new Dictionary<string, object?> { ["exchangeSegment"] = exchangeSegment };
            return HandleResponse(await GetAsync("market.instruments.indexlist", p), "Get Index List");
        }

        /// <summary>Get series list for an exchange segment.</summary>
        public async Task<Dictionary<string, object?>> GetSeriesAsync(int exchangeSegment)
        {
            var p = new Dictionary<string, object?> { ["exchangeSegment"] = exchangeSegment };
            return HandleResponse(await GetAsync("market.instruments.instrument.series", p), "Get Series");
        }

        /// <summary>Get full equity symbol.</summary>
        public async Task<Dictionary<string, object?>> GetEquitySymbolAsync(
            int exchangeSegment, string series, string symbol)
        {
            var p = new Dictionary<string, object?>
            {
                ["exchangeSegment"] = exchangeSegment,
                ["series"]          = series,
                ["symbol"]          = symbol,
            };
            return HandleResponse(await GetAsync("market.instruments.instrument.equitysymbol", p), "Get Equity Symbol");
        }

        /// <summary>Get expiry dates for an instrument.</summary>
        public async Task<Dictionary<string, object?>> GetExpiryDateAsync(
            int exchangeSegment, string series, string symbol)
        {
            var p = new Dictionary<string, object?>
            {
                ["exchangeSegment"] = exchangeSegment,
                ["series"]          = series,
                ["symbol"]          = symbol,
            };
            return HandleResponse(await GetAsync("market.instruments.instrument.expirydate", p), "Get Expiry Date");
        }

        /// <summary>Get future symbol for an instrument.</summary>
        public async Task<Dictionary<string, object?>> GetFutureSymbolAsync(
            int exchangeSegment, string series, string symbol, string expiryDate)
        {
            var p = new Dictionary<string, object?>
            {
                ["exchangeSegment"] = exchangeSegment,
                ["series"]          = series,
                ["symbol"]          = symbol,
                ["expiryDate"]      = expiryDate,
            };
            return HandleResponse(await GetAsync("market.instruments.instrument.futuresymbol", p), "Get Future Symbol");
        }

        /// <summary>Get option symbol for an instrument.</summary>
        public async Task<Dictionary<string, object?>> GetOptionSymbolAsync(
            int    exchangeSegment,
            string series,
            string symbol,
            string expiryDate,
            string optionType,
            double strikePrice)
        {
            var p = new Dictionary<string, object?>
            {
                ["exchangeSegment"] = exchangeSegment,
                ["series"]          = series,
                ["symbol"]          = symbol,
                ["expiryDate"]      = expiryDate,
                ["optionType"]      = optionType,
                ["strikePrice"]     = strikePrice,
            };
            return HandleResponse(await GetAsync("market.instruments.instrument.optionsymbol", p), "Get Option Symbol");
        }

        /// <summary>Get available option types (CE/PE) for an instrument.</summary>
        public async Task<Dictionary<string, object?>> GetOptionTypeAsync(
            int exchangeSegment, string series, string symbol, string expiryDate)
        {
            var p = new Dictionary<string, object?>
            {
                ["exchangeSegment"] = exchangeSegment,
                ["series"]          = series,
                ["symbol"]          = symbol,
                ["expiryDate"]      = expiryDate,
            };
            return HandleResponse(await GetAsync("market.instruments.instrument.optiontype", p), "Get Option Type");
        }

        // ────────────────────────────────────────────────────────────────────
        // Market Data API — Search
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Search instruments by ID.
        /// <code>
        /// var instruments = new[] {
        ///     new { exchangeSegment = 2, exchangeInstrumentID = 47631 }
        /// };
        /// </code>
        /// </summary>
        public async Task<Dictionary<string, object?>> SearchByInstrumentIdAsync(object instruments)
        {
            var p = new Dictionary<string, object?>
            {
                ["source"]      = _source,
                ["instruments"] = instruments,
            };
            return HandleResponse(await PostAsync("market.search.instrumentsbyid", p), "Search by Instrument ID");
        }

        /// <summary>Search instruments by script name / string.</summary>
        public async Task<Dictionary<string, object?>> SearchByScriptnameAsync(string searchString)
        {
            var p = new Dictionary<string, object?> { ["searchString"] = searchString };
            return HandleResponse(await GetAsync("market.search.instrumentsbystring", p), "Search by Script Name");
        }
    }
}
