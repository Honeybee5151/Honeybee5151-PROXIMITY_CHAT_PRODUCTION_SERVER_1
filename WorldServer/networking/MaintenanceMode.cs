//8812938
namespace WorldServer.networking
{
    public static class MaintenanceMode
    {
        // Toggled via admin dashboard pub/sub or Redis key
        public static volatile bool Enabled = false;
    }
}
