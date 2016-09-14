using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using System.Security.Cryptography.X509Certificates;

namespace BusTrackWeb
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var host = new WebHostBuilder()
                .UseKestrel(options =>
                {
                    options.UseHttps(new X509Certificate2(ResourceManager.GetResourceLocation("cert.pfx")));
                })
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseIISIntegration()
                .UseStartup<Startup>().UseUrls("https://localhost", "https://192.168.1.140", "http://localhost", "http://192.168.1.140")
                .Build();

            host.Run();
        }
    }
}