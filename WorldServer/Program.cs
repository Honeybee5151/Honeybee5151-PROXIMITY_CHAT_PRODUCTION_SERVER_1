using WorldServer.core;
// hi
namespace WorldServer
{
    public sealed class Program
    {
        private static void Main(string[] args)
        {
            var server = new GameServer(args);

            // Optional: install console interceptor for admin dashboard logs
            if (System.Environment.GetEnvironmentVariable("ADMIN_DASHBOARD") == "true")
                RedisConsoleWriter.Install(server.Database.Conn, "admin:logs:worldserver");

            server.Run();
        }
    }
}
