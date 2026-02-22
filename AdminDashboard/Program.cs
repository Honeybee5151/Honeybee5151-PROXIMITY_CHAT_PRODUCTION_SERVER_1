using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using AdminDashboard.Services;
using System;

namespace AdminDashboard
{
    public sealed class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var service = builder.Services;

            service.AddSingleton<RedisService>();
            service.AddControllers();

            var app = builder.Build();

            // Initialize Redis connection
            var redis = app.Services.GetRequiredService<RedisService>();

            var isDocker = Environment.GetEnvironmentVariable("IS_DOCKER") != null;
            var configPath = isDocker ? "/data/admin.json" : "admin.json";
            var config = Shared.ServerConfig.ReadFile(configPath);

            var bindAddress = config.serverInfo.bindAddress;
            var port = config.serverInfo.port;

            app.Urls.Clear();
            app.Urls.Add($"http://{bindAddress}:{port}");

            // Token auth — stored in Redis, auto-generated on first boot
            var adminToken = redis.Database.StringGet("admin:token").ToString();
            if (string.IsNullOrEmpty(adminToken))
            {
                adminToken = Guid.NewGuid().ToString("N");
                redis.Database.StringSet("admin:token", adminToken);
                Console.WriteLine("============================================================");
                Console.WriteLine($"[AdminDashboard] Generated admin token: {adminToken}");
                Console.WriteLine("[AdminDashboard] Save this token — you need it to log in.");
                Console.WriteLine("[AdminDashboard] Token is stored in Redis key 'admin:token'");
                Console.WriteLine("============================================================");
            }
            else
            {
                Console.WriteLine($"[AdminDashboard] Using existing token from Redis (admin:token)");
            }

            app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value ?? "";

                // Allow static files and login page without auth
                if (!path.StartsWith("/api/"))
                {
                    await next();
                    return;
                }

                // Allow token validation endpoint
                if (path == "/api/auth/validate")
                {
                    await next();
                    return;
                }

                // Check Bearer token
                var authHeader = context.Request.Headers["Authorization"].ToString();
                if (authHeader == $"Bearer {adminToken}")
                {
                    await next();
                    return;
                }

                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Unauthorized");
            });

            app.UseStaticFiles();
            app.UseRouting();
            app.MapControllers();
            app.MapFallbackToFile("index.html");

            Console.WriteLine($"[AdminDashboard] Starting on http://{bindAddress}:{port}");
            app.Run();
        }
    }
}
