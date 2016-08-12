using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.IO;
using Newtonsoft.Json.Linq;
using NpgsqlTypes;
using BusTrackWeb.Models;

namespace BusTrackWeb
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true);

            if (env.IsEnvironment("Development"))
            {
                // This will push telemetry data through Application Insights pipeline faster, allowing you to view results immediately.
                builder.AddApplicationInsightsSettings(developerMode: true);
            }

            builder.AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.
            services.AddApplicationInsightsTelemetry(Configuration);

            services.AddMvc();

            services.AddEntityFrameworkNpgsql();
            //services.AddDbContext<tfgContext>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            app.UseApplicationInsightsRequestTelemetry();

            app.UseApplicationInsightsExceptionTelemetry();

            app.UseMvc();

            // Add a seed
            using (var context = new TFGContext())
            {
                if (context.Bus.Any() || context.Line.Any() || context.Stop.Any() || context.Travel.Any() || context.User.Any()) return; // Already seeded

                string json;
                using (StreamReader sr = File.OpenText(env.ContentRootPath + Path.DirectorySeparatorChar + "Models" + Path.DirectorySeparatorChar + "emt.json"))
                {
                    json = sr.ReadToEnd();
                }
                var all = JObject.Parse(json);

                var stops = all["stops"].Children();
                foreach (JToken token in stops)
                {
                    string[] location = token.SelectToken("position").ToString().Split('&');
                    Stop newStop = new Stop
                    {
                        id = token.SelectToken("id").ToObject<int>(),
                        position = new NpgsqlPoint(double.Parse(location[0]), double.Parse(location[1]))
                    };
                    context.Stop.Add(newStop);
                }
                context.SaveChanges();

                var lines = all["lines"].Children();
                foreach (JToken token in lines)
                {
                    Line newLine = new Line
                    {
                        id = token.SelectToken("id").ToObject<int>(),

                        name = token.SelectToken("name").ToString()
                    };

                    foreach (JToken id in token.SelectToken("stops").Children())
                    {
                        Stop s = (from stop in context.Stop
                                  where stop.id == id.ToObject<int>()
                                  select stop).First();
                        LineHasStop ls = new LineHasStop
                        {
                            Line = newLine,
                            Stop = s
                        };
                        newLine.LineStops.Add(ls);
                        s.LineStops.Add(ls);
                    }
                }
                context.SaveChanges();

                /*User user = context.User.First();
                Bus bus = context.Bus.Include(b => b.Line).ThenInclude(l => l.LineStops).ThenInclude(ls => ls.Stop).First();
                Line line = bus.Line;
                var stops = line.LineStops.ToList();
                Stop init = stops[0].Stop, end = stops[1].Stop;
                Travel tr = new Travel
                {
                    Bus = bus,
                    date = DateTime.Now,
                    distance = 600,
                    End = end,
                    Init = init,
                    Line = line,
                    time = 587,
                    User = user
                };
                context.Travel.Add(tr);*/

                /*Line line = context.Line.Single(l => l.id == 22);
                Bus bus = new Bus
                {
                    lastRefresh = DateTime.UtcNow,
                    Line = line,
                    mac = "maaaaaaaaaaaaaaaaaaaaaaaaik"
                };
                line.Buses.Add(bus);*/

                /*User user = new User
                {
                    email = "email@algo.net",
                    hash = "sad3272iue1q98suh32",
                    name = "nombrecito"
                };
                context.User.Add(user);*/

                //context.SaveChanges();
            }
        }
    }
}
