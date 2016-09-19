using BusTrackWeb.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

// For more information on enabling MVC for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace BusTrackWeb.Controllers
{
    [Route("/")]
    public class HomeController : Controller
    {
        private readonly JsonSerializerSettings _serializer;

        public HomeController()
        {
            _serializer = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            };
        }

        [AllowAnonymous]
        public async Task<ActionResult> Index([FromQuery] string responseType = "ui")
        {
            await GetData(responseType); // Get statistics
            switch (responseType)
            {
                case "json":
                    return new OkObjectResult(JsonConvert.SerializeObject(ViewData, _serializer));

                default:
                case "ui":
                    return View();
            }
        }

        [Route("/getpiedata")]
        [AllowAnonymous]
        public string GetPieData()
        {
            JArray array = new JArray();
            using (var context = new TFGContext())
            {
                foreach (var line in context.Line.Include(l => l.Travels))
                {
                    JObject obj = new JObject();
                    obj["label"] = line.id.ToString() + " - " + line.name;
                    obj["value"] = line.Travels.Count;
                    array.Add(obj);
                }
            }
            return array.ToString(Formatting.None);
        }

        /// <summary>
        /// Gets all statistics and adds it to the ViewData dictionary.
        /// </summary>
        private async Task GetData(string type)
        {
            Task<int> totalTravelsTask = Task.Factory.StartNew(() =>
            {
                // Total travels
                using (var context = new TFGContext())
                {
                    return context.Travel.Count();
                }
            });
            Task<double> travelsDayTask = Task.Factory.StartNew(() =>
            {
                // Travels by day
                return new Statistics().MapReduceTravelsByDay();
            });
            Task<long> mostUsedLineTask = Task.Factory.StartNew(() =>
            {
                // Most used line
                using (var context = new TFGContext())
                {
                    var query = context.Travel.Where(t => t.Line != null).GroupBy(t => t.lineId).OrderByDescending(l => l.Count());
                    return query.Any() ? query.First().Key : 0L;
                }
            });
            Task<double> averageDurationTask = Task.Factory.StartNew(() =>
            {
                // Average travel duration
                using (var context = new TFGContext())
                {
                    var query = context.Travel.Select(t => t.time);
                    return query.Any() ? query.Average() : 0D;
                }
            });
            Task<int> longestDurationTask = Task.Factory.StartNew(() =>
            {
                // Longest travel duration
                using (var context = new TFGContext())
                {
                    var query = context.Travel.Select(t => t.time);
                    return query.Any() ? query.Max() : 0;
                }
            });
            Task<double> pollutionBusTask = Task.Factory.StartNew(() =>
            {
                // Saved pollution with a normal bus
                using (var context = new TFGContext())
                {
                    double sub = Statistics.POLLUTION_CAR - Statistics.POLLUTION_BUS;
                    var query = context.Travel.Where(t => t.distance != 0);
                    return query.Any() ? query.Select(t => t.distance).ToList().Aggregate(0D, (a, b) => a + (b * sub)) : 0D;
                }
            });
            Task<double> pollutionEBusTask = Task.Factory.StartNew(() =>
            {
                // Saved pollution with an electrical bus
                using (var context = new TFGContext())
                {
                    double sub = Statistics.POLLUTION_CAR - Statistics.POLLUTION_BUS_E;
                    var query = context.Travel.Where(t => t.distance != 0);
                    return query.Any() ? query.Select(t => t.distance).ToList().Aggregate(0D, (a, b) => a + (b * sub)) : 0D;
                }
            });
            Task<string> buslongestTimeLineTask = Task.Factory.StartNew(() =>
            {
                using (var context = new TFGContext())
                {
                    var query = context.Bus.Select(b => b.lastRefresh);
                    return query.Any() ? query.Min().ToString(CultureInfo.InvariantCulture) : "";
                }
            });
            Task<TimeSpan> averageBusRefreshTask = Task.Factory.StartNew(() =>
            {
                using (var context = new TFGContext())
                {
                    var query = context.Bus.Select(b => b.lastRefresh.Ticks);
                    return query.Any() ? (DateTime.Now - new DateTime((long)query.Average())) : TimeSpan.Zero;
                }
            });
            Task<long> longestDistanceTask = Task.Factory.StartNew(() =>
            {
                using (var context = new TFGContext())
                {
                    var query = context.Travel.Where(t => t.distance > 0);
                    return query.Any() ? query.Select(t => t.distance).Max() : 0;
                }
            });
            Task<double> averageDistanceTask = Task.Factory.StartNew(() =>
            {
                using (var context = new TFGContext())
                {
                    var query = context.Travel.Where(t => t.distance > 0);
                    return query.Any() ? query.Select(t => t.distance).Average() : 0;
                }
            });
            Task<int> mostPopularWDayTask = Task.Factory.StartNew(() =>
            {
                return new Statistics().MapReduceTravelsDayWeek();
            });

            // Wait for all tasks
            await Task.WhenAll(totalTravelsTask, travelsDayTask, mostUsedLineTask, averageDurationTask, longestDurationTask, pollutionBusTask, pollutionEBusTask, buslongestTimeLineTask, averageBusRefreshTask, longestDistanceTask, averageDistanceTask, mostPopularWDayTask);

            if (type.Equals("json"))
            {
                ViewData["TotalTravels"] = totalTravelsTask.Result;
                ViewData["TravelsDay"] = travelsDayTask.Result;
                ViewData["MostUsedLine"] = mostUsedLineTask.Result;
                ViewData["AverageDuration"] = averageDurationTask.Result;
                ViewData["LongestDuration"] = longestDurationTask.Result;
                ViewData["PollutionBus"] = pollutionBusTask.Result;
                ViewData["PollutionEBus"] = pollutionEBusTask.Result;
                ViewData["LongestBusNonRefreshed"] = buslongestTimeLineTask.Result;
                ViewData["AverageBusRefresh"] = averageBusRefreshTask.Result;
                ViewData["LongesDistance"] = longestDistanceTask.Result;
                ViewData["AverageDistance"] = averageDistanceTask.Result;
                ViewData["MostPopularDay"] = mostPopularWDayTask.Result != -1 ? CultureInfo.InvariantCulture.DateTimeFormat.GetDayName((DayOfWeek) Enum.ToObject(typeof(DayOfWeek), mostPopularWDayTask.Result)) : "N/A";
            }
            else
            {
                ViewData["Viajes totales"] = totalTravelsTask.Result.ToString() + " viajes";
                ViewData["Viajes por día"] = travelsDayTask.Result.ToString(CultureInfo.InvariantCulture) + " viajes";
                ViewData["Línea más usada"] = mostUsedLineTask.Result != 0 ? "Línea " + mostUsedLineTask.Result.ToString() : "Ninguna";
                ViewData["Duración media de viaje"] = (averageDurationTask.Result >= 3600 ? Math.Round(averageDurationTask.Result / 3600D).ToString(CultureInfo.InvariantCulture) : Math.Round(averageDurationTask.Result / 60D).ToString(CultureInfo.InvariantCulture)) + (averageDurationTask.Result >= 3600 ? " horas" : " minutos");
                ViewData["Viaje más largo"] = (longestDurationTask.Result >= 3600 ? Math.Round(longestDurationTask.Result / 3600D).ToString(CultureInfo.InvariantCulture) : Math.Round(longestDurationTask.Result / 60D).ToString(CultureInfo.InvariantCulture)) + (longestDurationTask.Result >= 3600 ? " horas" : " minutos");
                ViewData["Contaminación ahorrada (Bus normal)"] = Math.Round(pollutionBusTask.Result).ToString(CultureInfo.InvariantCulture) + "g CO2/km";
                ViewData["Contaminación ahorrada (Bus eléctrico)"] = Math.Round(pollutionEBusTask.Result).ToString(CultureInfo.InvariantCulture) + "g CO2/km";
                ViewData["Actualización más antigua de bus"] = buslongestTimeLineTask.Result.Length != 0 ? buslongestTimeLineTask.Result : "Ninguna";
                ViewData["Actualización media de buses"] = averageBusRefreshTask.Result;
                ViewData["Distancia más larga"] = longestDistanceTask.Result.ToString() + " metros";
                ViewData["Distancia media"] = averageDistanceTask.Result.ToString(CultureInfo.InvariantCulture) + " metros";
                ViewData["Día más popular"] = mostPopularWDayTask.Result != -1 ? new CultureInfo("es-ES").DateTimeFormat.GetDayName((DayOfWeek)Enum.ToObject(typeof(DayOfWeek), mostPopularWDayTask.Result)) : "Ninguno";
            }
        }
    }
}