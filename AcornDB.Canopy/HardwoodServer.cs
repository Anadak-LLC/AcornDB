
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System;

namespace AcornDB.Models
{
    public static class HardwoodServerExtensions
    {
        // Extended Grow implementation for Canopy project
        public static void GrowWithServer(this Hardwood hardwood, Grove grove)
        {
            var server = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseUrls($"http://*:{hardwood.Port}");
                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapPost("/stash/{type}/{id}", async context =>
                            {
                                var type = context.Request.RouteValues["type"]?.ToString();
                                var id = context.Request.RouteValues["id"]?.ToString();
                                if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(id))
                                {
                                    context.Response.StatusCode = 400;
                                    await context.Response.WriteAsync("Missing type or id");
                                    return;
                                }

                                var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
                                if (!grove.TryStash(type, id, body))
                                {
                                    context.Response.StatusCode = 500;
                                    await context.Response.WriteAsync("Failed to stash nut.");
                                    return;
                                }

                                context.Response.StatusCode = 200;
                                await context.Response.WriteAsync("Stashed!");
                            });

                            endpoints.MapPost("/toss/{type}/{id}", async context =>
                            {
                                var type = context.Request.RouteValues["type"]?.ToString();
                                var id = context.Request.RouteValues["id"]?.ToString();
                                if (!grove.TryToss(type, id))
                                {
                                    context.Response.StatusCode = 404;
                                    await context.Response.WriteAsync("Nut not found.");
                                    return;
                                }

                                context.Response.StatusCode = 200;
                                await context.Response.WriteAsync("Tossed!");
                            });

                            endpoints.MapGet("/crack/{type}/{id}", async context =>
                            {
                                var type = context.Request.RouteValues["type"]?.ToString();
                                var id = context.Request.RouteValues["id"]?.ToString();
                                var result = grove.TryCrack(type, id);
                                if (result == null)
                                {
                                    context.Response.StatusCode = 404;
                                    await context.Response.WriteAsync("Nut not found.");
                                    return;
                                }

                                context.Response.ContentType = "application/json";
                                await context.Response.WriteAsync(result);
                            });

                            endpoints.MapGet("/shake", async context =>
                            {
                                grove.ShakeAll();
                                await context.Response.WriteAsync("Shaken, not stirred.");
                            });
                        });
                    });
                }).Build();

            server.Start();
        }
    }
}
