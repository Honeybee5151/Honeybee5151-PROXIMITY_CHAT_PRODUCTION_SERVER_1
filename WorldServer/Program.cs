using Shared;

namespace WorldServer
{
    public sealed class Program
    {
        private static void Main(string[] args)
        {
            var server = new GameServer(args);

            if (System.Environment.GetEnvironmentVariable("ADMIN_DASHBOARD") == "true")
                RedisConsoleWriter.Install(server.Database.Conn, "admin:logs:worldserver");

            server.Run();
        }
    }
}
