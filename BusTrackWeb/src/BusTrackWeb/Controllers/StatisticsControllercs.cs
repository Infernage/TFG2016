using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using BusTrackWeb.Models;

// For more information on enabling MVC for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace BusTrackWeb.Controllers
{
    [Route("statistics/[action]")]
    public class StatisticsControllercs : Controller
    {
        private readonly JsonSerializerSettings _serializer;

        public StatisticsControllercs()
        {
            _serializer = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            };
        }

        [AllowAnonymous]
        public async Task<ActionResult> Index([FromQuery] string responseType = "ui")
        {
            await GetData(); // Get statistics
            switch (responseType)
            {
                case "json":
                    return new OkObjectResult(JsonConvert.SerializeObject(ViewData, _serializer));
                default:
                case "ui":
                    return View();
            }
        }

        /// <summary>
        /// Gets all statistics and adds it to the ViewData dictionary.
        /// </summary>
        private async Task GetData()
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
                    return context.Travel.Select(t => t.time).Average();
                }
            });
            Task<int> longestDurationTask = Task.Factory.StartNew(() =>
            {
                // Longest travel duration
                using (var context = new TFGContext())
                {
                    return context.Travel.Select(t => t.time).Max();
                }
            });
            Task<double> pollutionBusTask = Task.Factory.StartNew(() =>
            {
                // Saved pollution with a normal bus
                using (var context = new TFGContext())
                {
                    double sub = Statistics.POLLUTION_CAR - Statistics.POLLUTION_BUS;
                    return context.Travel.Where(t => t.distance != 0).Select(t => t.distance).Aggregate(0D, (a, b) => a + (b * sub));
                }
            });
            Task<double> pollutionEBusTask = Task.Factory.StartNew(() =>
            {
                // Saved pollution with an electrical bus
                using (var context = new TFGContext())
                {
                    double sub = Statistics.POLLUTION_CAR - Statistics.POLLUTION_BUS_E;
                    return context.Travel.Where(t => t.distance != 0).Select(t => t.distance).Aggregate(0D, (a, b) => a + (b * sub));
                }
            });

            // Wait for all tasks
            await Task.WhenAll(totalTravelsTask, travelsDayTask, mostUsedLineTask, averageDurationTask, longestDurationTask, pollutionBusTask, pollutionEBusTask);

            ViewData["TotalTravels"] = totalTravelsTask.Result;
            ViewData["TravelsDay"] = travelsDayTask.Result;
            ViewData["MostUsedLine"] = mostUsedLineTask.Result;
            ViewData["AverageDuration"] = averageDurationTask.Result;
            ViewData["LongestDuration"] = longestDurationTask.Result;
            ViewData["PollutionBus"] = pollutionBusTask.Result;
            ViewData["PollutionEBus"] = pollutionEBusTask.Result;
        }
    }
}
