//8812938
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AdminDashboard.Services
{
    public class RedisService
    {
        private ConnectionMultiplexer _multiplexer;
        private IDatabase _db;
        private IServer _server;

        public RedisService()
        {
            var isDocker = Environment.GetEnvironmentVariable("IS_DOCKER") != null;
            var configPath = isDocker ? "/data/admin.json" : "admin.json";
            var config = Shared.ServerConfig.ReadFile(configPath);

            var conString = config.dbInfo.host + ":" + config.dbInfo.port + ",syncTimeout=120000,allowAdmin=true";
            if (!string.IsNullOrWhiteSpace(config.dbInfo.auth))
                conString += ",password=" + config.dbInfo.auth;

            _multiplexer = ConnectionMultiplexer.Connect(conString);
            _db = _multiplexer.GetDatabase(config.dbInfo.index);
            _server = _multiplexer.GetServer(config.dbInfo.host, config.dbInfo.port);

            Console.WriteLine($"[RedisService] Connected to {config.dbInfo.host}:{config.dbInfo.port}");
        }

        public IDatabase Database => _db;
        public IServer Server => _server;
        public ConnectionMultiplexer Multiplexer => _multiplexer;

        /// <summary>
        /// Get parsed Redis INFO sections
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> GetInfo()
        {
            var info = _server.Info();
            var result = new Dictionary<string, Dictionary<string, string>>();

            foreach (var group in info)
            {
                var section = new Dictionary<string, string>();
                foreach (var entry in group)
                    section[entry.Key] = entry.Value;
                result[group.Key] = section;
            }

            return result;
        }

        /// <summary>
        /// SCAN keys with pattern, returns page of results using raw SCAN command
        /// </summary>
        public (List<string> Keys, long Cursor) ScanKeys(string pattern, int count, long cursor)
        {
            var keys = new List<string>();

            // Use raw SCAN for proper cursor-based pagination
            var result = _db.Execute("SCAN", cursor.ToString(), "MATCH", pattern, "COUNT", count.ToString());
            var arr = (RedisResult[])result;
            var nextCursor = long.Parse((string)arr[0]);
            var keyArr = (RedisResult[])arr[1];

            foreach (var key in keyArr)
                keys.Add(key.ToString());

            return (keys, nextCursor);
        }

        /// <summary>
        /// Get key type and value
        /// </summary>
        public (string Type, object Value) GetKeyValue(string key)
        {
            var type = _db.KeyType(key);

            switch (type)
            {
                case RedisType.String:
                    return ("string", _db.StringGet(key).ToString());

                case RedisType.Hash:
                    var hash = _db.HashGetAll(key);
                    var dict = hash.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
                    return ("hash", dict);

                case RedisType.List:
                    var list = _db.ListRange(key, 0, -1);
                    return ("list", list.Select(v => v.ToString()).ToArray());

                case RedisType.Set:
                    var set = _db.SetMembers(key);
                    return ("set", set.Select(v => v.ToString()).ToArray());

                case RedisType.SortedSet:
                    var zset = _db.SortedSetRangeByRankWithScores(key, 0, -1);
                    var entries = zset.Select(e => new { Member = e.Element.ToString(), e.Score }).ToArray();
                    return ("sortedset", entries);

                default:
                    return ("unknown", null);
            }
        }

        /// <summary>
        /// Get key TTL
        /// </summary>
        public long GetKeyTtl(string key)
        {
            var ttl = _db.KeyTimeToLive(key);
            return ttl.HasValue ? (long)ttl.Value.TotalSeconds : -1;
        }

        //8812938 — write operations for Redis browser

        /// <summary>Set a string key value</summary>
        public void StringSet(string key, string value)
        {
            _db.StringSet(key, value);
        }

        /// <summary>Set a single hash field</summary>
        public void HashSet(string key, string field, string value)
        {
            _db.HashSet(key, field, value);
        }

        /// <summary>Delete a hash field</summary>
        public bool HashDelete(string key, string field)
        {
            return _db.HashDelete(key, field);
        }

        /// <summary>Set a list element by index</summary>
        public void ListSet(string key, int index, string value)
        {
            _db.ListSetByIndex(key, index, value);
        }

        /// <summary>Push to end of list</summary>
        public void ListPush(string key, string value)
        {
            _db.ListRightPush(key, value);
        }

        /// <summary>Remove a list element by value</summary>
        public long ListRemove(string key, string value)
        {
            return _db.ListRemove(key, value, 1);
        }

        /// <summary>Add a set member</summary>
        public bool SetAdd(string key, string value)
        {
            return _db.SetAdd(key, value);
        }

        /// <summary>Remove a set member</summary>
        public bool SetRemove(string key, string value)
        {
            return _db.SetRemove(key, value);
        }

        /// <summary>Add/update sorted set member</summary>
        public bool SortedSetAdd(string key, string member, double score)
        {
            return _db.SortedSetAdd(key, member, score);
        }

        /// <summary>Remove sorted set member</summary>
        public bool SortedSetRemove(string key, string member)
        {
            return _db.SortedSetRemove(key, member);
        }

        /// <summary>Delete an entire key</summary>
        public bool DeleteKey(string key)
        {
            return _db.KeyDelete(key);
        }

        /// <summary>Set expiry on a key</summary>
        public bool KeyExpire(string key, TimeSpan expiry)
        {
            return _db.KeyExpire(key, expiry);
        }

        /// <summary>Add a new hash field</summary>
        public void HashAddField(string key, string field, string value)
        {
            _db.HashSet(key, field, value);
        }

        //8812938 — admin command helpers

        /// <summary>Ban an account (sets banned flag, notes, lift time in Redis)</summary>
        public void BanAccount(int accountId, string reason, int liftTime = -1)
        {
            var key = $"account.{accountId}";
            _db.HashSet(key, new HashEntry[]
            {
                new("banned", "True"),
                new("notes", reason ?? ""),
                new("banLiftTime", liftTime.ToString())
            });
        }

        /// <summary>Unban an account</summary>
        public void UnbanAccount(int accountId)
        {
            var key = $"account.{accountId}";
            _db.HashSet(key, new HashEntry[]
            {
                new("banned", "False"),
                new("banLiftTime", "0")
            });
        }

        /// <summary>Ban an IP address via the 'ips' hash</summary>
        public void BanIp(string ip, string notes = "")
        {
            var json = _db.HashGet("ips", ip);
            var ipInfo = json.IsNullOrEmpty
                ? new { Accounts = new List<int>(), Notes = notes, Banned = true }
                : (dynamic)Newtonsoft.Json.JsonConvert.DeserializeObject<IpInfoDto>((string)json);

            if (ipInfo is IpInfoDto dto)
            {
                dto.Banned = true;
                dto.Notes = notes;
                _db.HashSet("ips", ip, Newtonsoft.Json.JsonConvert.SerializeObject(dto));
            }
            else
            {
                _db.HashSet("ips", ip, Newtonsoft.Json.JsonConvert.SerializeObject(
                    new IpInfoDto { Accounts = new List<int>(), Notes = notes, Banned = true }));
            }
        }

        /// <summary>Unban an IP address</summary>
        public void UnbanIp(string ip)
        {
            var json = _db.HashGet("ips", ip);
            if (json.IsNullOrEmpty) return;

            var dto = Newtonsoft.Json.JsonConvert.DeserializeObject<IpInfoDto>((string)json);
            dto.Banned = false;
            _db.HashSet("ips", ip, Newtonsoft.Json.JsonConvert.SerializeObject(dto));
        }

        /// <summary>Mute an IP (optional duration)</summary>
        public void MuteIp(string ip, TimeSpan? duration = null)
        {
            _db.StringSet($"mutes:{ip}", "", duration);
        }

        /// <summary>Unmute an IP</summary>
        public void UnmuteIp(string ip)
        {
            _db.KeyDelete($"mutes:{ip}");
        }

        /// <summary>Get the IP for an account ID</summary>
        public string GetAccountIp(int accountId)
        {
            return HashGet($"account.{accountId}", "ip");
        }

        //8812938 — player lookup for Redis browser

        /// <summary>Resolve a player name to account ID via the 'names' hash</summary>
        public string ResolveAccountId(string name)
        {
            var val = _db.HashGet("names", name.ToUpperInvariant());
            return val.IsNullOrEmpty ? null : val.ToString();
        }

        /// <summary>Get a single hash field value</summary>
        public string HashGet(string key, string field)
        {
            var val = _db.HashGet(key, field);
            return val.IsNullOrEmpty ? null : val.ToString();
        }

        /// <summary>Get all related keys for an account ID</summary>
        public List<string> GetAccountRelatedKeys(string accountId)
        {
            var keys = new List<string>();
            // account hash
            if (_db.KeyExists($"account.{accountId}"))
                keys.Add($"account.{accountId}");
            // vault
            if (_db.KeyExists($"vault.{accountId}"))
                keys.Add($"vault.{accountId}");
            // class stats
            if (_db.KeyExists($"classStats.{accountId}"))
                keys.Add($"classStats.{accountId}");
            // alive characters set
            if (_db.KeyExists($"alive.{accountId}"))
                keys.Add($"alive.{accountId}");
            // dead characters list
            if (_db.KeyExists($"dead.{accountId}"))
                keys.Add($"dead.{accountId}");
            // scan for char.{id}.* keys
            foreach (var key in _server.Keys(pattern: $"char.{accountId}.*", pageSize: 50))
                keys.Add(key.ToString());
            return keys;
        }

        /// <summary>
        /// Get Redis memory usage summary
        /// </summary>
        public Dictionary<string, string> GetMemoryStats()
        {
            var info = GetInfo();
            var result = new Dictionary<string, string>();

            if (info.TryGetValue("Memory", out var memory))
            {
                if (memory.TryGetValue("used_memory_human", out var usedMem))
                    result["used_memory"] = usedMem;
                if (memory.TryGetValue("used_memory_peak_human", out var peakMem))
                    result["peak_memory"] = peakMem;
                if (memory.TryGetValue("total_system_memory_human", out var totalMem))
                    result["total_system_memory"] = totalMem;
            }

            if (info.TryGetValue("Clients", out var clients))
            {
                if (clients.TryGetValue("connected_clients", out var connClients))
                    result["connected_clients"] = connClients;
            }

            if (info.TryGetValue("Server", out var server))
            {
                if (server.TryGetValue("uptime_in_seconds", out var uptime))
                    result["uptime_seconds"] = uptime;
                if (server.TryGetValue("redis_version", out var version))
                    result["redis_version"] = version;
            }

            if (info.TryGetValue("Keyspace", out var keyspace))
            {
                foreach (var db in keyspace)
                    result[$"db_{db.Key}"] = db.Value;
            }

            return result;
        }

        /// <summary>Execute a raw Redis command and return the result as a string</summary>
        public string ExecuteCommand(string commandLine)
        {
            var parts = ParseCommandLine(commandLine);
            if (parts.Length == 0) return "(error) empty command";

            var cmd = parts[0].ToUpper();
            var args = parts.Skip(1).Select(a => (object)a).ToArray();

            try
            {
                var result = _db.Execute(cmd, args);
                return FormatRedisResult(result);
            }
            catch (Exception ex)
            {
                return $"(error) {ex.Message}";
            }
        }

        private string[] ParseCommandLine(string input)
        {
            var parts = new List<string>();
            var current = "";
            bool inQuote = false;
            char quoteChar = '"';

            for (int i = 0; i < input.Length; i++)
            {
                var c = input[i];
                if (inQuote)
                {
                    if (c == quoteChar) inQuote = false;
                    else current += c;
                }
                else if (c == '"' || c == '\'')
                {
                    inQuote = true;
                    quoteChar = c;
                }
                else if (c == ' ')
                {
                    if (current.Length > 0) { parts.Add(current); current = ""; }
                }
                else current += c;
            }
            if (current.Length > 0) parts.Add(current);
            return parts.ToArray();
        }

        private string FormatRedisResult(StackExchange.Redis.RedisResult result)
        {
            if (result.IsNull) return "(nil)";
            var type = result.Type;
            if (type == StackExchange.Redis.ResultType.Integer) return $"(integer) {result}";
            if (type == StackExchange.Redis.ResultType.BulkString) return result.ToString();
            if (type == StackExchange.Redis.ResultType.SimpleString) return result.ToString();
            if (type == StackExchange.Redis.ResultType.MultiBulk)
            {
                var arr = (StackExchange.Redis.RedisResult[])result;
                if (arr.Length == 0) return "(empty array)";
                var lines = new List<string>();
                for (int i = 0; i < arr.Length; i++)
                    lines.Add($"{i + 1}) {FormatRedisResult(arr[i])}");
                return string.Join("\n", lines);
            }
            return result.ToString();
        }
    }

    public class IpInfoDto
    {
        public List<int> Accounts { get; set; } = new();
        public string Notes { get; set; } = "";
        public bool Banned { get; set; }
    }
}
