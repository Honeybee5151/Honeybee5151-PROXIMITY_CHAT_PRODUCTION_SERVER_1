//8812938
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
        /// SCAN keys with pattern, returns page of results
        /// </summary>
        public (List<string> Keys, long Cursor) ScanKeys(string pattern, int count, long cursor)
        {
            var keys = new List<string>();
            int fetched = 0;

            foreach (var key in _server.Keys(pattern: pattern, pageSize: count, cursor: cursor))
            {
                keys.Add(key.ToString());
                fetched++;
                if (fetched >= count) break;
            }

            return (keys, keys.Count < count ? 0 : cursor + count);
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
                    var list = _db.ListRange(key, 0, 100);
                    return ("list", list.Select(v => v.ToString()).ToArray());

                case RedisType.Set:
                    var set = _db.SetMembers(key);
                    return ("set", set.Select(v => v.ToString()).ToArray());

                case RedisType.SortedSet:
                    var zset = _db.SortedSetRangeByRankWithScores(key, 0, 100);
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
    }
}
