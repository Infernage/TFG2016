﻿using BusTrackWeb.Models;
using BusTrackWeb.TokenProvider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using NpgsqlTypes;
using System;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

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

            // Use a daily task to clear access token black list
            Timer timer = new Timer(x =>
            {
                OAuthTokenProvider.BlackList.Clear();
            }, null, (new TimeSpan(24, 0, 0) - DateTime.Now.TimeOfDay), TimeSpan.FromHours(24));

            builder.AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.
            services.AddApplicationInsightsTelemetry(Configuration);

            services.AddOptions();

            services.AddMvc(config =>
           {
               var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
               config.Filters.Add(new AuthorizeFilter(policy));
           });

            services.AddAuthorization();

            services.AddEntityFrameworkNpgsql();

            var jwtOpts = Configuration.GetSection(nameof(OAuthOptions));
            services.Configure<OAuthOptions>(options =>
            {
                options.Issuer = jwtOpts[nameof(OAuthOptions.Issuer)];
                options.Audience = jwtOpts[nameof(OAuthOptions.Audience)];
                options.SigningCredentials = new SigningCredentials(signKey, SecurityAlgorithms.HmacSha256);
            });
        }

        private static readonly string key = "tfgBusTrackWebRestFUL";
        private readonly SymmetricSecurityKey signKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            // Configure automatic validator
            var jwtSettOpts = Configuration.GetSection(nameof(OAuthOptions));
            // Parameters for custom signature validator
            var innerTokenParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtSettOpts[nameof(OAuthOptions.Issuer)],

                ValidateAudience = true,
                ValidAudience = jwtSettOpts[nameof(OAuthOptions.Audience)],

                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signKey,

                RequireExpirationTime = true,
                ValidateLifetime = true,

                ClockSkew = TimeSpan.Zero
            };
            var tokenParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtSettOpts[nameof(OAuthOptions.Issuer)],

                ValidateAudience = true,
                ValidAudience = jwtSettOpts[nameof(OAuthOptions.Audience)],

                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signKey,

                RequireExpirationTime = true,
                ValidateLifetime = true,

                ClockSkew = TimeSpan.Zero,
                SignatureValidator = (token, parameters) =>
                {
                    if (OAuthTokenProvider.BlackList.Contains(token)) return null;

                    var handler = new JwtSecurityTokenHandler();
                    SecurityToken sToken;
                    handler.ValidateToken(token, innerTokenParameters, out sToken);

                    return sToken;
                }
            };

            app.UseJwtBearerAuthentication(new JwtBearerOptions
            {
                AutomaticAuthenticate = true,
                AutomaticChallenge = true,
                TokenValidationParameters = tokenParameters
            });

            app.UseApplicationInsightsRequestTelemetry();

            app.UseApplicationInsightsExceptionTelemetry();

            app.UseMvc();

            app.UseStaticFiles();

            // Add a seed
            using (var context = new TFGContext())
            {
                if (context.Bus.Any() || context.Line.Any() || context.Stop.Any() || context.Travel.Any() || context.User.Any()) return; // Already seeded

                string json;
                using (StreamReader sr = File.OpenText(ResourceManager.GetResourceLocation("Models.emt.json")))
                {
                    json = sr.ReadToEnd();
                }
                var all = JObject.Parse(json);

                var stops = all["stops"].Children();
                foreach (JToken token in stops)
                {
                    string[] location = token["position"].ToString().Split('&');
                    Stop newStop = new Stop
                    {
                        id = token["id"].ToObject<int>(),
                        position = new NpgsqlPoint(double.Parse(location[0], CultureInfo.InvariantCulture), double.Parse(location[1], CultureInfo.InvariantCulture))
                    };
                    context.Stop.Add(newStop);
                }
                context.SaveChanges();

                var lines = all["lines"].Children();
                foreach (JToken token in lines)
                {
                    Line newLine = new Line
                    {
                        id = token["id"].ToObject<int>(),

                        name = token["name"].ToString()
                    };

                    foreach (JToken id in token["stops"].Children())
                    {
                        Stop s = (from stop in context.Stop
                                  where stop.id == id.ToObject<long>()
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
            }
        }
    }
}