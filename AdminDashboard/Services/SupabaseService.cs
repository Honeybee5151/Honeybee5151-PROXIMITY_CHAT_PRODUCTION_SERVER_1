using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace AdminDashboard.Services
{
    public class SupabaseService
    {
        private readonly HttpClient _http;
        private readonly string _url;
        private readonly string _serviceKey;

        public SupabaseService()
        {
            _url = Environment.GetEnvironmentVariable("SUPABASE_URL") ?? "";
            _serviceKey = Environment.GetEnvironmentVariable("SUPABASE_SERVICE_KEY") ?? "";

            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("apikey", _serviceKey);
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _serviceKey);

            if (string.IsNullOrEmpty(_url) || string.IsNullOrEmpty(_serviceKey))
                Console.WriteLine("[SupabaseService] WARNING: SUPABASE_URL or SUPABASE_SERVICE_KEY not set");
            else
                Console.WriteLine($"[SupabaseService] Configured for {_url}");
        }

        public bool IsConfigured => !string.IsNullOrEmpty(_url) && !string.IsNullOrEmpty(_serviceKey);

        /// <summary>Get all dungeons with given status</summary>
        public async Task<List<JObject>> GetDungeons(string status)
        {
            var res = await _http.GetAsync($"{_url}/rest/v1/dungeons?status=eq.{status}&order=created_at.desc&select=*");
            var json = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
                throw new Exception($"Supabase error: {res.StatusCode} - {json}");
            return JsonConvert.DeserializeObject<List<JObject>>(json);
        }

        /// <summary>Get a single dungeon by ID</summary>
        public async Task<JObject> GetDungeon(string id)
        {
            var res = await _http.GetAsync($"{_url}/rest/v1/dungeons?id=eq.{id}&select=*");
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync();
            var list = JsonConvert.DeserializeObject<List<JObject>>(json);
            return list?.Count > 0 ? list[0] : null;
        }

        /// <summary>Delete a dungeon by ID</summary>
        public async Task DeleteDungeon(string id)
        {
            var req = new HttpRequestMessage(HttpMethod.Delete, $"{_url}/rest/v1/dungeons?id=eq.{id}");
            req.Headers.Add("apikey", _serviceKey);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serviceKey);
            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync();
                throw new Exception($"Supabase delete error: {res.StatusCode} - {err}");
            }
        }

        /// <summary>Get pending (ungranted) purchases for a player</summary>
        public async Task<List<JObject>> GetPendingPurchases(string gameName)
        {
            var url = $"{_url}/rest/v1/purchases?game_name=eq.{Uri.EscapeDataString(gameName)}&granted_in_game=eq.false&select=*";
            var res = await _http.GetAsync(url);
            var json = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
            {
                Console.WriteLine($"[Supabase] GetPendingPurchases error: {res.StatusCode} - {json}");
                return new List<JObject>();
            }
            return JsonConvert.DeserializeObject<List<JObject>>(json) ?? new List<JObject>();
        }

        /// <summary>Mark a purchase as granted in-game</summary>
        public async Task MarkPurchaseGranted(string stripeSessionId)
        {
            var content = new StringContent(
                JsonConvert.SerializeObject(new { granted_in_game = true }),
                Encoding.UTF8, "application/json"
            );
            content.Headers.Add("Prefer", "return=minimal");
            var req = new HttpRequestMessage(HttpMethod.Patch,
                $"{_url}/rest/v1/purchases?stripe_session_id=eq.{Uri.EscapeDataString(stripeSessionId)}")
            {
                Content = content
            };
            req.Headers.Add("apikey", _serviceKey);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serviceKey);
            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync();
                Console.WriteLine($"[Supabase] MarkPurchaseGranted error: {res.StatusCode} - {err}");
            }
        }

        /// <summary>Update dungeon status</summary>
        public async Task UpdateStatus(string id, string status)
        {
            var content = new StringContent(
                JsonConvert.SerializeObject(new { status }),
                Encoding.UTF8, "application/json"
            );
            content.Headers.Add("Prefer", "return=minimal");
            var req = new HttpRequestMessage(HttpMethod.Patch, $"{_url}/rest/v1/dungeons?id=eq.{id}")
            {
                Content = content
            };
            req.Headers.Add("apikey", _serviceKey);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serviceKey);
            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync();
                throw new Exception($"Supabase update error: {res.StatusCode} - {err}");
            }
        }
    }
}
