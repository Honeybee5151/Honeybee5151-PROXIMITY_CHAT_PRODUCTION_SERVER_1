using Shared;
using WorldServer.core;

namespace WorldServer
{
    public sealed class Program
    {
        private static void Main(string[] args)
        {
            var server = new GameServer(args);

            //8812938
            if (System.Environment.GetEnvironmentVariable("ADMIN_DASHBOARD") == "true")
                RedisConsoleWriter.Install(server.Database.Conn, "admin:logs:worldserver");

            server.Run();
        }
    }
}
