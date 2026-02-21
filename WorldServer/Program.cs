using WorldServer.core;
// hi
namespace WorldServer
{
    public sealed class Program
    {
        private static void Main(string[] args)
        {
            var server = new GameServer(args);

            // Install console interceptor for admin dashboard logs
            RedisConsoleWriter.Install(server.Database.Conn, "admin:logs:worldserver");

            server.Run();
        }
    }
}
