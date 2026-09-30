using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Kubuno.Rust.Cargo.Registry
{
    /// <summary>
    /// Read-only access to crates.io for the crate manager and the Dependencies node's update/yanked
    /// markers: search through the web API, versions and features through the sparse index (the CDN
    /// cargo itself uses, which has no rate limit to speak of). Results are cached for the session
    /// (<see cref="CacheLifetime"/>), and every call is cancellable and time-limited, so an offline
    /// machine only loses the online parts.
    /// </summary>
    public sealed class CratesIoClient
    {
        /// <summary>crates.io's data access policy asks for a User-Agent that identifies the tool.</summary>
        public const string UserAgent = "Kubuno-VisualStudio (+https://github.com/kubuno/vskubuno)";

        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);
        private static readonly Lazy<HttpClient> SharedHttp = new Lazy<HttpClient>(CreateHttpClient);

        private readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<CrateIndexVersion>>> _indexCache =
            new ConcurrentDictionary<string, CacheEntry<IReadOnlyList<CrateIndexVersion>>>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, CacheEntry<CrateDetails>> _detailsCache =
            new ConcurrentDictionary<string, CacheEntry<CrateDetails>>(StringComparer.OrdinalIgnoreCase);

        private readonly HttpClient _http;

        public CratesIoClient()
            : this(SharedHttp.Value)
        {
        }

        public CratesIoClient(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        /// <summary>The instance the whole extension shares (one cache, one connection pool).</summary>
        public static CratesIoClient Shared { get; } = new CratesIoClient();

        public async Task<CrateSearchPage> SearchAsync(string query, int page, int perPage, CancellationToken cancellationToken)
        {
            // No query: the most downloaded crates (NuGet's Browse tab opens on the popular packages).
            var url = "https://crates.io/api/v1/crates?q=" + Uri.EscapeDataString(query ?? string.Empty)
                + (string.IsNullOrWhiteSpace(query) ? "&sort=downloads" : string.Empty)
                + "&page=" + Math.Max(1, page).ToString(CultureInfo.InvariantCulture)
                + "&per_page=" + Math.Max(1, Math.Min(100, perPage)).ToString(CultureInfo.InvariantCulture);
            var json = await GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            return CratesIoParser.ParseSearch(json);
        }

        /// <summary>Every published version of <paramref name="crateName"/>, oldest first; empty when the crate does not exist.</summary>
        public async Task<IReadOnlyList<CrateIndexVersion>> GetVersionsAsync(string crateName, CancellationToken cancellationToken)
        {
            if (_indexCache.TryGetValue(crateName, out var cached) && cached.IsFresh)
            {
                return cached.Value;
            }

            var url = "https://index.crates.io/" + CratesIoParser.IndexPath(crateName);
            string? text = await GetStringOrNullWhenMissingAsync(url, cancellationToken).ConfigureAwait(false);
            var versions = text is null ? Array.Empty<CrateIndexVersion>() : CratesIoParser.ParseIndex(text);
            _indexCache[crateName] = new CacheEntry<IReadOnlyList<CrateIndexVersion>>(versions);
            return versions;
        }

        public async Task<CrateDetails?> GetDetailsAsync(string crateName, CancellationToken cancellationToken)
        {
            if (_detailsCache.TryGetValue(crateName, out var cached) && cached.IsFresh)
            {
                return cached.Value;
            }

            var json = await GetStringOrNullWhenMissingAsync("https://crates.io/api/v1/crates/" + Uri.EscapeDataString(crateName), cancellationToken).ConfigureAwait(false);
            if (json is null)
            {
                return null;
            }

            var details = CratesIoParser.ParseCrate(json);
            _detailsCache[crateName] = new CacheEntry<CrateDetails>(details);
            return details;
        }

        /// <summary>Drops the cached index data (after an install/update, or on an explicit refresh).</summary>
        public void Invalidate()
        {
            _indexCache.Clear();
            _detailsCache.Clear();
        }

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return client;
        }

        private async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
        {
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        private async Task<string?> GetStringOrNullWhenMissingAsync(string url, CancellationToken cancellationToken)
        {
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        private sealed class CacheEntry<T>
        {
            private readonly DateTime _createdUtc = DateTime.UtcNow;

            public CacheEntry(T value)
            {
                Value = value;
            }

            public T Value { get; }

            public bool IsFresh => DateTime.UtcNow - _createdUtc < CacheLifetime;
        }
    }
}
