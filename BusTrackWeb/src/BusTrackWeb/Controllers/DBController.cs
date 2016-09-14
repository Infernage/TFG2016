using BusTrackWeb.Models;
using BusTrackWeb.TokenProvider;
using GeoCoordinatePortable;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

// For more information on enabling Web API for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace BusTrackWeb.Controllers
{
    [Route("backend/[action]")]
    public class DBController : Controller
    {
        private readonly OAuthOptions _options;
        private readonly ILogger _logger;
        private readonly JsonSerializerSettings _serializer;

        public DBController(IOptions<OAuthOptions> opts, ILoggerFactory logger)
        {
            _options = opts.Value;
            OAuthTokenProvider.ThrowIfInvalidOptions(_options);

            _logger = logger.CreateLogger<OAuthTokenProvider>();

            _serializer = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            };
        }

        [HttpGet]
        [Authorize]
        public ActionResult All()
        {
            using (var context = new TFGContext())
            {
                var root = new JObject();

                // Insert buses
                root["buses"] = Buses();

                // Insert lines
                root["lines"] = Lines();

                // Insert stops
                root["stops"] = Stops();

                return new OkObjectResult(root.ToString(Formatting.Indented));
            }
        }

        #region Buses

        [HttpGet]
        [Authorize]
        public JArray Buses()
        {
            using (var context = new TFGContext())
            {
                var jsonBuses = new JArray();
                foreach (Bus b in context.Bus)
                {
                    JObject token = new JObject();
                    token[nameof(Bus.mac)] = b.mac;
                    token[nameof(Bus.lastRefresh)] = b.lastRefresh;
                    token["line"] = b.lineId;
                    jsonBuses.Add(token);
                }
                return jsonBuses;
            }
        }

        [HttpGet("{mac}")]
        [Authorize]
        public ActionResult Buses(string mac)
        {
            using (var context = new TFGContext())
            {
                var query = context.Bus.Where(b => b.mac.Equals(mac));
                if (!query.Any()) return NotFound();

                return new ObjectResult(query.First());
            }
        }

        [HttpPost]
        [Authorize]
        public ActionResult Buses([FromBody] Bus item)
        {
            using (var context = new TFGContext())
            {
                if (item == null || item.mac.Length == 0) return BadRequest();

                var query = context.Bus.Where(b => b.mac.Equals(item.mac));
                if (query.Any()) item = query.First();
                else
                {
                    context.Bus.Add(item);
                    var q = context.Line.Include(l => l.Buses).Where(l => l.id == item.lineId);
                    if (q.Any())
                    {
                        Line line = q.First();
                        item.Line = line;
                        if (!line.Buses.Contains(item)) line.Buses.Add(item);
                    }
                    context.SaveChanges();
                }

                return CreatedAtAction("Buses", new { mac = item.mac }, item);
            }
        }

        [HttpPut("{mac}")]
        [Authorize]
        public ActionResult Buses(string mac, [FromBody] Bus item)
        {
            using (var context = new TFGContext())
            {
                if (item == null || item.mac.Length == 0 || !mac.Equals(item.mac)) return BadRequest();

                var query = context.Bus.Where(b => b.mac.Equals(item.mac));
                if (!query.Any()) return NotFound();
                Bus bus = query.First();
                if (bus.lastRefresh < item.lastRefresh) bus.lastRefresh = item.lastRefresh;
                if (bus.lineId != item.lineId)
                {
                    var q = context.Line.Include(l => l.Buses).Where(l => l.id == item.lineId);
                    if (q.Any())
                    {
                        Line line = q.First();
                        bus.Line = line;
                        if (!line.Buses.Contains(bus)) line.Buses.Add(bus);
                    }
                }
                context.SaveChanges();

                return new NoContentResult();
            }
        }

        #endregion Buses

        #region Lines

        [HttpGet]
        [Authorize]
        public JArray Lines()
        {
            using (var context = new TFGContext())
            {
                var jsonLines = new JArray();
                foreach (Line l in context.Line.Include(li => li.LineStops).Include(li => li.Buses))
                {
                    JObject token = new JObject();
                    token[nameof(Line.id)] = l.id;
                    token[nameof(Line.name)] = l.name;

                    JArray lineStops = new JArray();
                    foreach (LineHasStop ls in l.LineStops)
                    {
                        lineStops.Add(ls.stop_id);
                    }
                    token["stops"] = lineStops;

                    JArray lineBuses = new JArray();
                    foreach (Bus b in l.Buses)
                    {
                        lineBuses.Add(b.mac);
                    }
                    token["buses"] = lineBuses;

                    jsonLines.Add(token);
                }
                return jsonLines;
            }
        }

        [HttpGet("{line}")]
        [Authorize]
        public ActionResult Lines(long line)
        {
            using (var context = new TFGContext())
            {
                var query = context.Line.Include(l => l.LineStops).Where(l => l.id == line);
                if (!query.Any()) return NotFound();

                return new ObjectResult(query.First());
            }
        }

        [HttpPost]
        [Authorize]
        public ActionResult Lines([FromBody] Line item)
        {
            using (var context = new TFGContext())
            {
                if (item == null || item.id == 0) return BadRequest();

                var query = context.Line.Where(l => l.id == item.id);
                if (query.Any()) item = query.First();
                else
                {
                    context.Line.Add(item);
                    context.SaveChanges();
                }

                return CreatedAtAction("Lines", new { line = item.id }, item);
            }
        }

        [HttpPut("{line}")]
        [Authorize]
        public ActionResult Lines(long line, [FromBody] Line item)
        {
            using (var context = new TFGContext())
            {
                if (item == null || item.id == 0 || line != item.id) return BadRequest();

                var query = context.Line.Where(li => li.id == line);
                if (!query.Any()) return NotFound();
                Line l = query.First();
                if (!l.name.Equals(item.name)) l.name = item.name;
                context.SaveChanges();

                return new NoContentResult();
            }
        }

        #endregion Lines

        #region Stops

        [HttpGet]
        [Authorize]
        public JArray Stops()
        {
            using (var context = new TFGContext())
            {
                var jsonStops = new JArray();
                foreach (Stop s in context.Stop.Include(st => st.LineStops))
                {
                    JObject token = new JObject();
                    token[nameof(Stop.latitude)] = s.latitude;
                    token[nameof(Stop.longitude)] = s.longitude;
                    token[nameof(Stop.id)] = s.id;

                    JArray stopLines = new JArray();
                    foreach (LineHasStop ls in s.LineStops)
                    {
                        stopLines.Add(ls.line_id);
                    }
                    token["lines"] = stopLines;

                    jsonStops.Add(token);
                }
                return jsonStops;
            }
        }

        [HttpGet("{stop}")]
        [Authorize]
        public ActionResult Stops(long stop)
        {
            using (var context = new TFGContext())
            {
                var query = context.Stop.Include(s => s.LineStops).Where(s => s.id == stop);
                if (!query.Any()) return NotFound();

                return new ObjectResult(query.First());
            }
        }

        [HttpPost]
        [Authorize]
        public ActionResult Stops([FromBody] Stop item)
        {
            using (var context = new TFGContext())
            {
                if (item == null) return BadRequest();

                var query = context.Stop.Where(s => new GeoCoordinate(item.latitude, item.longitude)
                                                        .GetDistanceTo(new GeoCoordinate(s.position.X, s.position.Y)) <= 5);
                if (query.Any())
                {
                    List<Stop> sorted = query.ToList();
                    sorted.Sort(new PointComparer(item));
                    item = sorted.First();
                }
                else
                {
                    context.Stop.Add(item);
                    context.SaveChanges();
                }

                return CreatedAtAction("Stops", new { stop = item.id }, item);
            }
        }

        [HttpPut("{stop}")]
        [Authorize]
        public ActionResult Stops(long stop, [FromBody] Stop item)
        {
            using (var context = new TFGContext())
            {
                if (item == null || item.id == 0 || stop != item.id) return BadRequest();

                var query = context.Stop.Where(st => st.id == stop);
                if (!query.Any()) return NotFound();
                Stop s = query.First();
                if (s.latitude != item.latitude) s.latitude = item.latitude;
                if (s.longitude != item.longitude) s.longitude = item.longitude;
                context.SaveChanges();

                return new NoContentResult();
            }
        }

        #endregion Stops

        #region LineStop

        [HttpPost]
        [Authorize]
        public ActionResult LineStops([FromBody] LineHasStop item)
        {
            using (var context = new TFGContext())
            {
                if (item == null || item.line_id == 0 || item.stop_id == 0) return BadRequest();

                var query = context.LineHasStop.Where(ls => ls.line_id == item.line_id && ls.stop_id == item.stop_id);
                if (query.Any()) item = query.First();
                else
                {
                    var ql = context.Line.Where(l => l.id == item.line_id);
                    var qs = context.Stop.Where(s => s.id == item.stop_id);
                    if (ql.Any() && qs.Any())
                    {
                        context.LineHasStop.Add(item);
                        Line line = ql.First();
                        item.Line = line;
                        line.LineStops.Add(item);
                        Stop stop = qs.First();
                        item.Stop = stop;
                        stop.LineStops.Add(item);
                        context.SaveChanges();
                    }
                }

                return Ok();
            }
        }

        #endregion LineStop
    }

    internal class PointComparer : IComparer<Stop>
    {
        private Stop item;

        public PointComparer(Stop t)
        {
            item = t;
        }

        public int Compare(Stop x, Stop y)
        {
            double disX = new GeoCoordinate(item.latitude, item.longitude).GetDistanceTo(new GeoCoordinate(x.position.X, x.position.Y));
            double disY = new GeoCoordinate(item.latitude, item.longitude).GetDistanceTo(new GeoCoordinate(y.position.X, y.position.Y));

            return disX.CompareTo(disY);
        }
    }
}