using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DexcomOverlay.Models;

namespace DexcomOverlay.Services;

/// <summary>
/// Client that communicates directly with the Dexcom Share API
/// to retrieve real-time glucose readings.
/// </summary>
public sealed class DexcomShareClient : IDisposable
{
    // ── Base URLs ──────────────────────────────────────────────
    private static readonly Dictionary<string, string> BaseUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["us"]  = "https://share2.dexcom.com/ShareWebServices/Services/",
        ["ous"] = "https://shareous1.dexcom.com/ShareWebServices/Services/",
        ["jp"]  = "https://share.dexcom.jp/ShareWebServices/Services/",
    };

    // ── Application IDs ────────────────────────────────────────
    private static readonly Dictionary<string, string> ApplicationIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["us"]  = "d89443d2-327c-4a6f-89e5-496bbb0317db",
        ["ous"] = "d89443d2-327c-4a6f-89e5-496bbb0317db",
        ["jp"]  = "d8665ade-9673-4e27-9ff6-92db4ce13d13",
    };

    // ── Endpoints ──────────────────────────────────────────────
    private const string AuthenticateEndpoint  = "General/AuthenticatePublisherAccount";
    private const string LoginEndpoint         = "General/LoginPublisherAccountById";
    private const string GlucoseReadingsEndpoint = "Publisher/ReadPublisherLatestGlucoseValues";

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _applicationId;
    private readonly string _username;
    private readonly string _password;

    private string? _accountId;
    private string? _sessionId;

    // ── Cache ──────────────────────────────────────────────────
    private List<GlucoseReading>? _cachedReadings;
    private DateTime _cacheTimestamp = DateTime.MinValue;
    private int _cachedMinutes;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    public DexcomShareClient(string username, string password, string region = "us")
    {
        _username = username;
        _password = password;

        // Validate region — reject unknown values rather than silently defaulting
        if (!BaseUrls.ContainsKey(region))
            throw new ArgumentException($"Unknown region '{region}'. Supported: us, ous, jp", nameof(region));

        _baseUrl = BaseUrls[region];
        _applicationId = ApplicationIds[region];

        _http = new HttpClient();
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/json");
    }

    /// <summary>Get the current glucose reading (last 10 minutes, 1 result).</summary>
    public async Task<GlucoseReading?> GetCurrentReadingAsync(CancellationToken ct = default)
    {
        var readings = await GetGlucoseReadingsAsync(minutes: 10, maxCount: 1, ct).ConfigureAwait(false);
        return readings.FirstOrDefault();
    }

    /// <summary>Get glucose readings within the specified time window. Uses cache when possible.</summary>
    public async Task<List<GlucoseReading>> GetGlucoseReadingsAsync(
        int minutes = 1440, int maxCount = 288, CancellationToken ct = default)
    {
        // Return cached data if fresh enough and covers the requested window
        if (_cachedReadings is not null &&
            DateTime.UtcNow - _cacheTimestamp < CacheTtl &&
            _cachedMinutes >= minutes)
        {
            var cutoff = DateTime.Now.AddMinutes(-minutes);
            return _cachedReadings.Where(r => r.Timestamp >= cutoff).ToList();
        }

        await EnsureSessionAsync(ct).ConfigureAwait(false);

        // Always fetch the larger of requested or cached window to maximize cache hits
        int fetchMinutes = Math.Max(minutes, 1440);
        int fetchCount = fetchMinutes / 5 + 1;

        List<GlucoseReading> readings;
        try
        {
            readings = await FetchReadingsAsync(fetchMinutes, fetchCount, ct).ConfigureAwait(false);
        }
        catch (DexcomSessionException)
        {
            _sessionId = null;
            await EnsureSessionAsync(ct).ConfigureAwait(false);
            readings = await FetchReadingsAsync(fetchMinutes, fetchCount, ct).ConfigureAwait(false);
        }

        // Update cache
        _cachedReadings = readings.OrderBy(r => r.Timestamp).ToList();
        _cachedMinutes = fetchMinutes;
        _cacheTimestamp = DateTime.UtcNow;

        // Return only the requested window
        var requestCutoff = DateTime.Now.AddMinutes(-minutes);
        return _cachedReadings.Where(r => r.Timestamp >= requestCutoff).ToList();
    }

    /// <summary>Invalidate the cache so the next call fetches fresh data.</summary>
    public void InvalidateCache()
    {
        _cachedReadings = null;
        _cacheTimestamp = DateTime.MinValue;
    }

    // ── Private helpers ────────────────────────────────────────

    private async Task EnsureSessionAsync(CancellationToken ct)
    {
        if (_sessionId is not null) return;

        if (_accountId is null)
            _accountId = await AuthenticateAsync(ct).ConfigureAwait(false);

        _sessionId = await LoginAsync(ct).ConfigureAwait(false);
    }

    private async Task<string> AuthenticateAsync(CancellationToken ct)
    {
        var payload = new
        {
            accountName = _username,
            password = _password,
            applicationId = _applicationId,
        };

        var response = await _http.PostAsJsonAsync($"{_baseUrl}{AuthenticateEndpoint}", payload, ct)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        CheckForError(response, body);

        // Response is a JSON string (the account ID UUID)
        return JsonSerializer.Deserialize<string>(body)
            ?? throw new DexcomApiException("Empty account ID returned.");
    }

    private async Task<string> LoginAsync(CancellationToken ct)
    {
        var payload = new
        {
            accountId = _accountId,
            password = _password,
            applicationId = _applicationId,
        };

        var response = await _http.PostAsJsonAsync($"{_baseUrl}{LoginEndpoint}", payload, ct)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        CheckForError(response, body);

        return JsonSerializer.Deserialize<string>(body)
            ?? throw new DexcomApiException("Empty session ID returned.");
    }

    private async Task<List<GlucoseReading>> FetchReadingsAsync(
        int minutes, int maxCount, CancellationToken ct)
    {
        var url = $"{_baseUrl}{GlucoseReadingsEndpoint}" +
                  $"?sessionId={_sessionId}&minutes={minutes}&maxCount={maxCount}";

        var response = await _http.PostAsync(url, null, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        CheckForError(response, body);

        var jsonReadings = JsonSerializer.Deserialize<List<JsonElement>>(body) ?? [];
        var readings = new List<GlucoseReading>();

        foreach (var jr in jsonReadings)
        {
            var reading = ParseReading(jr);
            if (reading is not null) readings.Add(reading);
        }

        return readings;
    }

    internal static GlucoseReading? ParseReading(JsonElement json)
    {
        try
        {
            var value = json.GetProperty("Value").GetInt32();
            var trendStr = json.GetProperty("Trend").GetString() ?? "Flat";
            var dtStr = json.GetProperty("DT").GetString() ?? "";

            if (!GlucoseReading.TrendDirectionMap.TryGetValue(trendStr, out var trendIndex))
                trendIndex = 4; // default to Flat

            // Parse "Date(1691455258000-0400)" format
            DateTime timestamp = DateTime.Now;
            var match = Regex.Match(dtStr, @"Date\((\d+)([+-]\d{4})\)");
            if (match.Success)
            {
                var ms = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var offset = DateTimeOffset.ParseExact(
                    "20000101" + match.Groups[2].Value, "yyyyMMddzzz", CultureInfo.InvariantCulture);
                timestamp = DateTimeOffset.FromUnixTimeMilliseconds(ms)
                    .ToOffset(offset.Offset).DateTime;
            }

            return new GlucoseReading
            {
                Value = value,
                TrendDirection = trendStr,
                TrendIndex = trendIndex,
                Timestamp = timestamp,
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DexcomOverlay] Failed to parse glucose reading: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static void CheckForError(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode) return;

        // Try to parse Dexcom error JSON: {"Code": "...", "Message": "..."}
        try
        {
            var doc = JsonDocument.Parse(body);
            var code = doc.RootElement.GetProperty("Code").GetString() ?? "";

            if (code is "SessionIdNotFound" or "SessionNotValid")
                throw new DexcomSessionException(code);

            var message = doc.RootElement.GetProperty("Message").GetString() ?? code;
            throw new DexcomApiException(message);
        }
        catch (DexcomSessionException) { throw; }
        catch (DexcomApiException) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DexcomOverlay] Unexpected API error (HTTP {(int)response.StatusCode}): {ex.GetType().Name}: {ex.Message}");
            response.EnsureSuccessStatusCode(); // fallback
        }
    }

    public void Dispose() => _http.Dispose();
}

public class DexcomApiException(string message) : Exception(message);
public class DexcomSessionException(string code) : DexcomApiException($"Session error: {code}");
