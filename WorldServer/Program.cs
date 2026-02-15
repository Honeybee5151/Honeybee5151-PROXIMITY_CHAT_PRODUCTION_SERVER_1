using WorldServer.core;
// hi
namespace WorldServer
{
    public sealed class Program
    {
        private static void Main(string[] args)
        {
            new GameServer(args).Run();
        }
    }
}
