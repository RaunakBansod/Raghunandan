using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace XtsApiClient
{
    // Base variables class — holds session state after login.
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

    // The XTS Connect API wrapper class (async).
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
        private readonly ILogger<XtsConnect> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _root;
        private readonly string _apiKey;
        private readonly string _secretKey;
        private readonly string _source;
        private readonly bool _debug;
        private DateTimeOffset? _lastLoginTime;

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
            _root      = root;
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

        // Internal helpers
        private void SetCommonVariables(string accessToken, string userID, bool isInvestorClient)
        {
            Token           = accessToken;
            UserID          = userID;
            IsInvestorClient = isInvestorClient;
        }

        // Centralised response handler. Returns the response dictionary on success, throws on error.
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

        // Adds clientID to the params dict based on IsInvestorClient flag.
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

        // Core HTTP request dispatcher.
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

        // <summary>
        // Builds a URL-encoded query string from a dictionary.
        // </summary>
        private static string BuildQueryString(Dictionary<string, object?> p)
        {
            var parts = new List<string>();
            foreach (var (k, v) in p)
                if (v is not null)
                    parts.Add($"{Uri.EscapeDataString(k)}={Uri.EscapeDataString(v.ToString()!)}");
            return string.Join("&", parts);
        }

        // ────────────────────────────────────────────────────────────────────
        // Market Data API — Authentication
        // ────────────────────────────────────────────────────────────────────

        // Log in to the market data API
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

        // <summary>Log out from the market data API.</summary>
        public async Task<Dictionary<string, object?>> MarketdataLogoutAsync()
        {
            var response = await DeleteAsync("market.logout");
            Token = null;
            return HandleResponse(response, "Market Data Logout");
        }

        // ────────────────────────────────────────────────────────────────────
        // Market Data API — Configuration & Quotes
        // ────────────────────────────────────────────────────────────────────

        // <summary>Get client configuration.</summary>
        public async Task<Dictionary<string, object?>> GetConfigAsync()
            => HandleResponse(await GetAsync("market.config"), "Get Config");

        // <summary>Get live quote for instruments.</summary>
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

        // <summary>
        // Subscribe to live market data for a list of instruments.
        // xtsMessageCode examples: 1501=Touchline, 1502=MarketDepth, 1512=LTP.
        // </summary>
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

        // <summary>Unsubscribe from market data for a list of instruments.</summary>
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

        // <summary>Download instrument master for given exchange segments.</summary>
        public async Task<Dictionary<string, object?>> GetMasterAsync(List<int> exchangeSegmentList)
        {
            var p = new Dictionary<string, object?> { ["exchangeSegmentList"] = exchangeSegmentList };
            return HandleResponse(await PostAsync("market.instruments.master", p), "Get Master");
        }

        // <summary>Retrieve historical OHLC candle data.</summary>
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

        // <summary>Get the index list for an exchange segment.</summary>
        public async Task<Dictionary<string, object?>> GetIndexListAsync(int exchangeSegment)
        {
            var p = new Dictionary<string, object?> { ["exchangeSegment"] = exchangeSegment };
            return HandleResponse(await GetAsync("market.instruments.indexlist", p), "Get Index List");
        }

        // <summary>Get series list for an exchange segment.</summary>
        public async Task<Dictionary<string, object?>> GetSeriesAsync(int exchangeSegment)
        {
            var p = new Dictionary<string, object?> { ["exchangeSegment"] = exchangeSegment };
            return HandleResponse(await GetAsync("market.instruments.instrument.series", p), "Get Series");
        }

        // <summary>Get full equity symbol.</summary>
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

        // <summary>Get expiry dates for an instrument.</summary>
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

        // <summary>Get future symbol for an instrument.</summary>
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

        // <summary>Get option symbol for an instrument.</summary>
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

        // <summary>Get available option types (CE/PE) for an instrument.</summary>
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

        // <summary>
        // Search instruments by ID.
        // <code>
        // var instruments = new[] {
        //     new { exchangeSegment = 2, exchangeInstrumentID = 47631 }
        // };
        // </code>
        // </summary>
        public async Task<Dictionary<string, object?>> SearchByInstrumentIdAsync(object instruments)
        {
            var p = new Dictionary<string, object?>
            {
                ["source"]      = _source,
                ["instruments"] = instruments,
            };
            return HandleResponse(await PostAsync("market.search.instrumentsbyid", p), "Search by Instrument ID");
        }

        // <summary>Search instruments by script name / string.</summary>
        public async Task<Dictionary<string, object?>> SearchByScriptnameAsync(string searchString)
        {
            var p = new Dictionary<string, object?> { ["searchString"] = searchString };
            return HandleResponse(await GetAsync("market.search.instrumentsbystring", p), "Search by Script Name");
        }
    }
}