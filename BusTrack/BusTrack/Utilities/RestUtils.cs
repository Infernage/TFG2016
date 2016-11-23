using Android.Content;
using Android.Locations;
using Android.Util;
using BusTrack.Data;
using Java.Security.Cert;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Realms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BusTrack.Utilities
{
    public class RestUtils
    {
        private static readonly string WEB_URL = "https://bustrack.undo.it";
        private static volatile bool busy = false;

        /// <summary>
        /// Synchronizes local DB with remote one and flushes any unsynced data.
        /// </summary>
        /// <param name="context">Android context.</param>
        public static void Sync(Context context)
        {
            // Avoid more than 1 synchronization
            if (busy) return;
            busy = true;

            Thread thread = new Thread(() =>
            {
                try
                {
                    HttpResponseMessage response = CallWebAPI("/backend/all", CancellationToken.None, context, bearer: true).Result;
                    if (response.IsSuccessStatusCode)
                    {
                        var root = JObject.Parse(response.Content.ReadAsStringAsync().Result);
                        var buses = root["buses"];
                        var stops = root["stops"];
                        var lines = root["lines"];
                        using (Realm realm = Realm.GetInstance(Utils.GetDB()))
                        {
                            // Apply external data
                            realm.Write(() =>
                            {
                                Dictionary<string, Bus> sBuses = new Dictionary<string, Bus>();
                                Dictionary<long, Line> sLines = new Dictionary<long, Line>();

                                // Sync buses
                                foreach (JToken btoken in buses)
                                {
                                    string mac = btoken[nameof(Bus.mac)].ToString();
                                    var bquery = realm.All<Bus>().Where(b => b.mac == mac);
                                    Bus bus = bquery.Any() ? bquery.First() : realm.CreateObject<Bus>();
                                    if (bus.lastRefresh == null || bus.lastRefresh <= btoken[nameof(Bus.lastRefresh)].ToObject<DateTime>()) bus.lastRefresh = btoken[nameof(Bus.lastRefresh)].ToObject<DateTime>();
                                    if (!bquery.Any()) bus.mac = mac;
                                    sBuses.Add(bus.mac, bus);
                                }

                                // Sync lines
                                foreach (JToken ltoken in lines)
                                {
                                    long lid = ltoken[nameof(Line.id)].ToObject<long>();
                                    var lquery = realm.All<Line>().Where(l => l.id == lid);
                                    Line line = lquery.Any() ? lquery.First() : realm.CreateObject<Line>();
                                    if (line.name == null || !line.name.Equals(ltoken[nameof(Line.name)].ToString())) line.name = ltoken[nameof(Line.name)].ToString();
                                    if (!lquery.Any()) line.id = lid;

                                    foreach (JToken id in ltoken["buses"])
                                    {
                                        Bus bus = sBuses[id.ToString()];
                                        bus.line = line;
                                    }
                                    sLines.Add(line.id, line);
                                }

                                // Sync stops
                                foreach (JToken stoken in stops)
                                {
                                    long sid = stoken[nameof(Stop.id)].ToObject<long>();
                                    var squery = realm.All<Stop>().Where(s => s.id == sid);
                                    Stop stop = squery.Any() ? squery.First() : realm.CreateObject<Stop>();
                                    Location loc = new Location("");
                                    loc.Latitude = stoken["latitude"].ToObject<double>();
                                    loc.Longitude = stoken["longitude"].ToObject<double>();
                                    if (stop.location.Latitude != loc.Latitude || stop.location.Longitude != loc.Longitude) stop.location = loc;
                                    if (!squery.Any()) stop.id = sid;

                                    foreach (JToken id in stoken["lines"])
                                    {
                                        Line line = sLines[id.ToObject<long>()];
                                        if (!stop.lines.Contains(line)) stop.lines.Add(line);
                                        if (!line.stops.Contains(stop)) line.stops.Add(stop);
                                    }
                                    stop.synced = true;
                                }

                                // Set all new data as synced
                                sBuses.Values.ToList().ForEach(b => b.synced = true);
                                sLines.Values.ToList().ForEach(l => l.synced = true);
                            });

                            // Send unsynced local data
                            var qb = realm.All<Bus>().Where(b => b.synced == false);
                            var qs = realm.All<Stop>().Where(s => s.synced == false);
                            var ql = realm.All<Line>().Where(l => l.synced == false);

                            realm.Write(() =>
                            {
                                // Update buses
                                foreach (Bus bus in qb)
                                {
                                    if (!UpdateBus(context, bus).Result)
                                    {
                                        Bus b = CreateBus(context, bus).Result;
                                        if (b.synced)
                                        {
                                            bus.lineId = b.lineId;
                                            bus.line = realm.All<Line>().Where(l => l.id == b.lineId).First();
                                            bus.lastRefresh = b.lastRefresh;
                                            bus.synced = true;
                                        }
                                    }
                                }

                                // Update lines
                                foreach (Line line in ql)
                                {
                                    if (!UpdateLine(context, line).Result)
                                    {
                                        Line l = CreateLine(context, line).Result;
                                        if (l.synced)
                                        {
                                            line.name = l.name;
                                            foreach (long sid in l.jstops)
                                            {
                                                var q = realm.All<Stop>().Where(s => s.id == sid);
                                                if (q.Any())
                                                {
                                                    Stop stop = q.First();
                                                    if (!line.stops.Contains(stop)) line.stops.Add(stop);
                                                    if (!stop.lines.Contains(line)) stop.lines.Add(line);
                                                }
                                            }
                                            line.synced = true;
                                        }
                                    }
                                }

                                // Update stops
                                foreach (Stop stop in qs)
                                {
                                    Stop s = CreateStop(context, stop.location).Result;
                                    if (s.synced)
                                    {
                                        stop.location = s.location;
                                        foreach (long lid in s.jlines)
                                        {
                                            var q = realm.All<Line>().Where(l => l.id == lid);
                                            if (q.Any())
                                            {
                                                Line line = q.First();
                                                if (!stop.lines.Contains(line)) stop.lines.Add(line);
                                                if (!line.stops.Contains(stop)) line.stops.Add(stop);
                                            }
                                        }
                                        stop.synced = true;
                                    }
                                }
                            });
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Error("Synchronization", Java.Lang.Throwable.FromException(e), "Synchronization with server failed!");
                }
                busy = false;
            });
            thread.Start();
        }

        /// <summary>
        /// Creates a new Bus entity in the remote DB.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <param name="bus">The local entity served as data template.</param>
        /// <returns>The new entity with remote data or the local entity if something went wrong.</returns>
        public static async Task<Bus> CreateBus(Context context, Bus bus)
        {
            var content = new StringContent(JsonConvert.SerializeObject(bus), Encoding.UTF8, "application/json");

            HttpResponseMessage response = await CallWebAPI("/backend/buses", CancellationToken.None, context, content, timeout: 10, bearer: true);
            if (response.IsSuccessStatusCode)
            {
                bus = JsonConvert.DeserializeObject<Bus>(await response.Content.ReadAsStringAsync());
                bus.synced = true;
            }
            return bus;
        }

        /// <summary>
        /// Updates a Bus entity in the remote DB.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <param name="bus">The local entity with the data to be sent.</param>
        /// <returns>The success status.</returns>
        public static async Task<bool> UpdateBus(Context context, Bus bus)
        {
            var content = new StringContent(JsonConvert.SerializeObject(bus), Encoding.UTF8, "application/json");

            HttpResponseMessage response = await CallWebAPI($"/backend/buses/{bus.mac}", CancellationToken.None, context, content, timeout: 10, update: true, bearer: true);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Creates a new Line entity in the remote DB.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <param name="line">The local entity served as data template.</param>
        /// <returns>The new entity with remote data or the local entity if something went wrong.</returns>
        public static async Task<Line> CreateLine(Context context, Line line)
        {
            var content = new StringContent(JsonConvert.SerializeObject(line), Encoding.UTF8, "application/json");

            HttpResponseMessage response = await CallWebAPI("/backend/lines", CancellationToken.None, context, content, timeout: 10, bearer: true);
            if (response.IsSuccessStatusCode)
            {
                line = JsonConvert.DeserializeObject<Line>(await response.Content.ReadAsStringAsync());
                line.synced = true;
            }
            return line;
        }

        /// <summary>
        /// Updates a Line entity in the remote DB.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <param name="line">The local entity with the data to be sent.</param>
        /// <returns>The success status.</returns>
        public static async Task<bool> UpdateLine(Context context, Line line)
        {
            var content = new StringContent(JsonConvert.SerializeObject(line), Encoding.UTF8, "application/json");

            HttpResponseMessage response = await CallWebAPI($"/backend/lines/{line.id}", CancellationToken.None, context, content, timeout: 10, update: true, bearer: true);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Creates a new Stop entity in the remote DB.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <param name="location">The local entity served as data template.</param>
        /// <returns>The new entity with remote data or the local entity if something went wrong.</returns>
        public static async Task<Stop> CreateStop(Context context, Location location)
        {
            Stop stop = new Stop
            {
                location = location
            };
            var content = new StringContent(JsonConvert.SerializeObject(stop), Encoding.UTF8, "application/json");

            HttpResponseMessage response = await CallWebAPI("/backend/stops", CancellationToken.None, context, content, timeout: 10, bearer: true);
            if (response.IsSuccessStatusCode)
            {
                stop = JsonConvert.DeserializeObject<Stop>(await response.Content.ReadAsStringAsync());
                stop.synced = true;
            }
            return stop;
        }

        /// <summary>
        /// Updates a M:M relationship between a Line entity and a Stop entity.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <param name="line">The Line entity.</param>
        /// <param name="stop">The Stop entity.</param>
        /// <returns>The success status.</returns>
        public static async Task<bool> UpdateLineStop(Context context, Line line, Stop stop)
        {
            var json = new
            {
                line_id = line.id,
                stop_id = stop.id
            };
            var content = new StringContent(JsonConvert.SerializeObject(json), Encoding.UTF8, "application/json");

            HttpResponseMessage response = await CallWebAPI("/backend/linestops", CancellationToken.None, context, content, timeout: 10, bearer: true);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Creates an user travel in the remote DB.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <param name="travel">The local entity Travel with the data to be sent.</param>
        /// <param name="cts">A CancellationTokenSource, just in the case the user wants to cancel the operation.</param>
        /// <returns>The success status.</returns>
        public static async Task<bool> UploadTravel(Context context, Travel travel, CancellationTokenSource cts = null)
        {
            string str = JsonConvert.SerializeObject(travel);
            var content = new StringContent(str, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await CallWebAPI("/account/addtravel", cts?.Token ?? CancellationToken.None, context, content, timeout: 10, bearer: true);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Method in charge of performs requests to the web server.
        /// </summary>
        /// <param name="urlPath">The url path (excluding base url).</param>
        /// <param name="token">The cancellation token used for cancel the call.</param>
        /// <param name="context">Android context.</param>
        /// <param name="content">The request content (default to null). If it's not provided, a GET request will be used instead.</param>
        /// <param name="checkLogin">A flag indicating if before requesting the data, it should check whether OAuth tokens are valids or not.</param>
        /// <param name="timeout">The timeout in seconds.</param>
        /// <param name="update">A flag indicating if the request content is a new entity or not.</param>
        /// <param name="bearer">If it's true, it will authenticate the user token.</param>
        /// <returns>A HttpResponseMessage object.</returns>
        internal async static Task<HttpResponseMessage> CallWebAPI(string urlPath, CancellationToken token, Context context, HttpContent content = null,
            bool checkLogin = true, int timeout = 30, bool update = false, bool bearer = false)
        {
            //Since we are using a self-signed certificate, we have to tell android that web server certificate is valid
            Certificate cert;
            CertificateFactory cf = CertificateFactory.GetInstance("X.509"); // Web server certificate uses X.509 format
            using (var stream = context.Assets.Open("nginx.cert")) // Read certificate from assets
            {
                cert = cf.GenerateCertificate(stream);
            }

            // Add the certificate to the TrustedCerts list
            var handler = new CustomAndroidClientHandler();
            List<Certificate> certs = new List<Certificate>();
            certs.Add(cert);
            handler.TrustedCerts = certs;

            using (var client = new HttpClient(handler))
            {
                // Setup http client
                client.MaxResponseContentBufferSize = 256000;
                client.Timeout = TimeSpan.FromSeconds(timeout);

                if (bearer)
                {
                    if (checkLogin && !await OAuthUtils.CheckLogin(context))
                    {
                        throw new Exception("Relog required");
                    }
                    else if (!OAuthUtils.UserLogged(context)) return new HttpResponseMessage(HttpStatusCode.Unauthorized); // Ensure there is an user logged!

                    ISharedPreferences prefs = context.GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private);
                    if (!prefs.Contains(OAuthUtils.PREF_USER_TOKEN)) return new HttpResponseMessage(HttpStatusCode.Unauthorized);

                    // Set token inside authenticator client
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", JObject.Parse(prefs.GetString(OAuthUtils.PREF_USER_TOKEN, ""))["access_token"].ToString());
                }

                // Build URL
                var url = new StringBuilder(WEB_URL).Append(urlPath).ToString();

                try
                {
                    // Use POST, PUT or GET
                    if (content != null && !update) return await client.PostAsync(url, content, token);
                    else if (content != null && update) return await client.PutAsync(url, content, token);
                    else return await client.GetAsync(url, token);
                }
                catch (OperationCanceledException e)
                {
                    Log.Error(Utils.NAME_PREF, Java.Lang.Throwable.FromException(e), "CallWebAPI cancelled!");
                    HttpResponseMessage msg = new HttpResponseMessage(HttpStatusCode.RequestTimeout);
                    msg.Content = new StringContent(e.Message);
                    return msg;
                }
                catch (Exception e)
                {
                    Log.Error(Utils.NAME_PREF, Java.Lang.Throwable.FromException(e), "CallWebAPI failed!");
                    HttpResponseMessage msg = new HttpResponseMessage(HttpStatusCode.Conflict);
                    msg.Content = new StringContent(e.Message);
                    return msg;
                }
            }
        }
    }
}