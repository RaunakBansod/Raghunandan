using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
        // ── API Route Map ─────────────────────────────────────────────────────
        private static readonly Dictionary<string, string> Routes = new()
        {
            // Market Data
            ["market.login"]                                        = "/apimarketdata/auth/login",
            ["market.logout"]                                       = "/apimarketdata/auth/logout",
            ["market.instruments.subscription"]                     = "/apimarketdata/instruments/subscription",
            ["market.instruments.ohlc"]                             = "/apimarketdata/instruments/ohlc",
            ["market.search.instrumentsbystring"]                   = "/apimarketdata/search/instruments",
            ["market.instruments.instrument.futuresymbol"]         = "/apimarketdata/instruments/instrument/futureSymbol",
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

        // ────────────────────────────────────────────────────────────────────
        // Low-level HTTP verbs
        // ────────────────────────────────────────────────────────────────────

        private Task<Dictionary<string, object?>> GetAsync(string route, Dictionary<string, object?>? p = null)
            => RequestAsync(route, HttpMethod.Get, p);

        private Task<Dictionary<string, object?>> PostAsync(string route, object? body = null)
            => RequestAsync(route, HttpMethod.Post, body);

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
            bool hasBody = method == HttpMethod.Post;
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

        // Builds a URL-encoded query string from a dictionary.
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

        // Log out from the market data API.
        public async Task<Dictionary<string, object?>> MarketdataLogoutAsync()
        {
            var response = await DeleteAsync("market.logout");
            Token = null;
            return HandleResponse(response, "Market Data Logout");
        }

        // ────────────────────────────────────────────────────────────────────
        // Market Data API — Subscriptions
        // ────────────────────────────────────────────────────────────────────

        // Subscribe to live market data for a list of instruments.
        // xtsMessageCode examples: 1501=Touchline, 1502=MarketDepth, 1512=LTP.
        public async Task<Dictionary<string, object?>> SendSubscriptionAsync(
            List<Dictionary<string, int>> instruments, int xtsMessageCode)
        {
            var p = new Dictionary<string, object?>
            {
                ["instruments"] = instruments,
                ["xtsMessageCode"] = xtsMessageCode,
            };
            return HandleResponse(await PostAsync("market.instruments.subscription", p), "Send Subscription");
        }
        
        // Retrieve historical OHLC candle data.
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

        // Get expiry dates for an instrument.
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

        // Get future symbol for an instrument.
        public async Task<Dictionary<string, object?>> GetFutureSymbolAsync(
            int exchangeSegment, string series, string symbol, string expiryDate)
        {
            var p = new Dictionary<string, object?>
            {
                ["exchangeSegment"] = exchangeSegment,
                ["series"] = series,
                ["symbol"] = symbol,
                ["expiryDate"] = expiryDate,
            };
            return HandleResponse(await GetAsync("market.instruments.instrument.futuresymbol", p), "Get Future Symbol");
        }
        
        // Search instruments by script name / string
        public async Task<Dictionary<string, object?>> SearchByScriptnameAsync(string searchString)
        {
            var p = new Dictionary<string, object?> { ["searchString"] = searchString };
            return HandleResponse(await GetAsync("market.search.instrumentsbystring", p), "Search by Script Name");
        }
    }
}