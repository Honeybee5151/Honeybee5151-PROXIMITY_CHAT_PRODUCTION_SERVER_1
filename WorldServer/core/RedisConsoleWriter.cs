using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using StackExchange.Redis;

namespace WorldServer.core
{
    /// <summary>
    /// Intercepts Console.Out and pushes lines to a Redis list for the admin dashboard.
    /// Batches writes every 2 seconds to minimize Redis overhead.
    /// </summary>
    public class RedisConsoleWriter : TextWriter
    {
        private readonly TextWriter _original;
        private readonly IDatabase _db;
        private readonly string _key;
        private readonly ConcurrentQueue<string> _buffer = new();
        private readonly Timer _flushTimer;
        private const int MAX_LINES = 1000;
        private const int FLUSH_INTERVAL_MS = 2000;
        private const int MAX_BUFFER = 200;

        public override Encoding Encoding => _original.Encoding;

        public RedisConsoleWriter(TextWriter original, IDatabase db, string redisKey)
        {
            _original = original;
            _db = db;
            _key = redisKey;
            _flushTimer = new Timer(_ => Flush(), null, FLUSH_INTERVAL_MS, FLUSH_INTERVAL_MS);
        }

        public override void WriteLine(string value)
        {
            _original.WriteLine(value);

            if (_buffer.Count < MAX_BUFFER)
            {
                var line = $"[{DateTime.UtcNow:HH:mm:ss}] {value}";
                _buffer.Enqueue(line);
            }
        }

        public override void Write(string value)
        {
            _original.Write(value);
        }

        private void Flush()
        {
            try
            {
                var batch = new System.Collections.Generic.List<RedisValue>();
                while (_buffer.TryDequeue(out var line))
                    batch.Add(line);

                if (batch.Count == 0) return;

                foreach (var line in batch)
                    _db.ListLeftPush(_key, line, flags: CommandFlags.FireAndForget);

                _db.ListTrim(_key, 0, MAX_LINES - 1, CommandFlags.FireAndForget);
            }
            catch
            {
                // Don't let Redis errors crash the server
            }
        }

        public static void Install(IDatabase db, string redisKey)
        {
            var writer = new RedisConsoleWriter(Console.Out, db, redisKey);
            Console.SetOut(writer);
        }
    }
}
