using System.Linq;

using Android.App;
using Android.Content;
using BusTrack.Data;
using System.Net;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json;
using System.Text;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Realms;

namespace BusTrack.Utilities
{
    class Utils
    {
        public static readonly string PREF_DATA_LIMIT = "limitData";
        public static readonly string PREF_USER_ID = "userID";
        public static readonly string PREF_USER_TOKEN = "userTk";
        public static readonly string PREF_NETWORKS = "networks";
        public static readonly string NAME_PREF = "BusTrack";

        public static readonly string NAME_LCHOOSER = "LineChooser";
        public static readonly string NAME_LCREATOR = "LineCreator";
        public static readonly string WEB_URL = "http://192.168.1.140";

        public static readonly float POLLUTION_CAR = 119F, POLLUTION_BUS = 104F, POLLUTION_BUS_E = 18.6F;

        internal static long ToUnixEpochDate(DateTime date) => (long)Math.Round((date.ToUniversalTime() - new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero)).TotalSeconds);

        private static readonly string PREF_VALID_TOKEN = "validTo";
        private static readonly string DISTANCE_MATRIX =
            "https://maps.googleapis.com/maps/api/distancematrix/json?key=AIzaSyDVZGmOKBOdXIClT1ArDYuK3b3cGHZ6LJA&origins=<->origin<->&destinations=<->destination<->&mode=transit&transit_mode=bus";
        private static char base64PadCharacter = '=';
        private static char base64Character62 = '+';
        private static char base64Character63 = '/';
        private static char base64UrlCharacter62 = '-';
        private static char _base64UrlCharacter63 = '_';

        public static long GetDistance(Stop init, Stop end)
        {
            string apiUrl = DISTANCE_MATRIX.Replace("<->origin<->", init.locationString).Replace("<->destination<->", end.locationString);
            WebClient client = new WebClient();
            string json = client.DownloadString(apiUrl);
            client.Dispose();
            var parsed = JObject.Parse(json)["rows"].Children().First()["elements"].Children().First();
            long distance = 0;
            if (parsed.Contains("distance"))
            {
                distance = parsed["distance"]["value"].Value<long>();
            }
            return distance;
        }

        /// <summary>
        /// Gets the user statistics from the server.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <returns>A JSON string with the user statistics or an empty one if something went wrong.</returns>
        public async static Task<string> GetStatistics(Context context)
        {
            if (!UserLogged(context)) return string.Empty;

            using (Realm realm = Realm.GetInstance(NAME_PREF))
            {
                long id = context.GetSharedPreferences(NAME_PREF, FileCreationMode.Private).GetLong(PREF_USER_ID, -1);

                using (var client = new HttpClient())
                {
                    client.MaxResponseContentBufferSize = 256000;
                    client.Timeout = TimeSpan.FromSeconds(30);

                    var content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("id", id.ToString())
                    });

                    HttpResponseMessage response = await client.PostAsync(new StringBuilder(WEB_URL).Append("/account/getstatistics").ToString(), content);
                    string json = string.Empty;
                    if (response.IsSuccessStatusCode) json = await response.Content.ReadAsStringAsync();
                    return json;
                }
            }
        }

        /// <summary>
        /// Performs a login.
        /// </summary>
        /// <param name="user">User email.</param>
        /// <param name="pass">User password.</param>
        /// <param name="context">Android context.</param>
        /// <returns>True or false if response is successful.</returns>
        public async static Task<bool> Login(string user, string pass, Context context)
        {
            using (var client = new HttpClient())
            {
                client.MaxResponseContentBufferSize = 256000;
                client.Timeout = TimeSpan.FromSeconds(30);

                string nPass;
                using (var sha = SHA512.Create())
                {
                    string sign = EncodeURL64(sha.ComputeHash(Encoding.UTF8.GetBytes(user)));
                    nPass = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(pass + sign)));
                }

                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("username", user),
                    new KeyValuePair<string, string>("password", nPass)
                });

                HttpResponseMessage response = await client.PostAsync(new StringBuilder(WEB_URL)
                    .Append("/oauth/generate").ToString(), content);
                if (response.IsSuccessStatusCode)
                {
                    string token = await response.Content.ReadAsStringAsync();
                    var jsonResp = JObject.Parse(token);
                    ISharedPreferencesEditor edit = context.GetSharedPreferences(NAME_PREF, FileCreationMode.Private)
                        .Edit();
                    edit.PutInt(PREF_USER_ID, jsonResp["id"].ToObject<int>());
                    edit.PutLong(PREF_VALID_TOKEN, ToUnixEpochDate(DateTime.Now.AddSeconds(jsonResp["expires_in"].ToObject<double>())));
                    edit.PutString(PREF_USER_TOKEN, token);
                    edit.Commit();
                }
                return response.IsSuccessStatusCode;
            }
        }

        /// <summary>
        /// Checks if an user is logged.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <returns>True or false depending if an user is logged or not.</returns>
        public static bool UserLogged(Context context)
        {
            ISharedPreferences prefs = context.GetSharedPreferences(NAME_PREF, FileCreationMode.Private);
            return prefs.GetLong(PREF_USER_ID, -1) < 1;
        }

        /// <summary>
        /// Refreshes the user token.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <returns>True or false depending if the action was successful or not.</returns>
        public async static Task<bool> Refresh(Context context)
        {
            if (!UserLogged(context)) return false;

            ISharedPreferences prefs = context.GetSharedPreferences(NAME_PREF, FileCreationMode.Private);
            string token = prefs.GetString(PREF_USER_TOKEN, "");
            if (token.Length == 0) return false;

            string refresh = JObject.Parse(token)["access_token"].ToString();

            using (var client = new HttpClient())
            {
                client.MaxResponseContentBufferSize = 256000;
                client.Timeout = TimeSpan.FromSeconds(30);

                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("token", refresh)
                });

                HttpResponseMessage response = await client.PostAsync(new StringBuilder(WEB_URL)
                    .Append("/oauth/refresh").ToString(), content);
                if (response.IsSuccessStatusCode)
                {
                    token = await response.Content.ReadAsStringAsync();
                    var jsonResp = JObject.Parse(token);
                    ISharedPreferencesEditor edit = prefs.Edit();
                    edit.PutString(PREF_USER_TOKEN, token);
                    edit.PutLong(PREF_VALID_TOKEN, ToUnixEpochDate(DateTime.Now.AddSeconds(jsonResp["expires_in"].ToObject<double>())));
                    edit.Commit();
                }
                return response.IsSuccessStatusCode;
            }
        }

        /// <summary>
        /// Logouts the current user.
        /// </summary>
        /// <param name="context">Android context.</param>
        public static void Logout(Context context)
        {
            if (UserLogged(context))
            {
                ISharedPreferencesEditor edit = context.GetSharedPreferences(NAME_PREF, FileCreationMode.Private).Edit();
                edit.Remove(PREF_USER_ID);
                edit.Remove(PREF_USER_TOKEN);
                edit.Commit();
                context.StopService(new Intent(context, typeof(Scanner)));
            }
        }

        /// <summary>
        /// Registers a new user into the system.
        /// </summary>
        /// <param name="name">The user name.</param>
        /// <param name="email">The user email.</param>
        /// <param name="pass">The user password.</param>
        /// <param name="context">Android context.</param>
        /// <returns>A tuple with:
        /// - Item1: Flag indicating if the registration has been successful.
        /// - Item2: The response content (Only for errors).</returns>
        internal async static Task<Tuple<bool, string>> Register(string name, string email, string pass, Context context)
        {
            using (var client = new HttpClient())
            {
                client.MaxResponseContentBufferSize = 256000;
                client.Timeout = TimeSpan.FromSeconds(30);

                string nPass;
                using (var sha = SHA512.Create())
                {
                    string sign = EncodeURL64(sha.ComputeHash(Encoding.UTF8.GetBytes(email)));
                    nPass = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(pass + sign)));
                }

                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("name", name),
                    new KeyValuePair<string, string>("email", email),
                    new KeyValuePair<string, string>("password", nPass)
                });

                HttpResponseMessage response = await client.PostAsync(new StringBuilder(WEB_URL)
                    .Append("/oauth/register").ToString(), content);

                return new Tuple<bool, string>(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            }
        }

        /// <summary>
        /// Performs the forgot password action.
        /// </summary>
        /// <param name="email">The user email.</param>
        /// <param name="context">Android context.</param>
        /// <returns>A tuple with:
        /// - Item1: Flag indicating if the action has been successful.
        /// - Item2: The response content (Only for errors).</returns>
        internal async static Task<Tuple<bool, string>> Forgot(string email, Context context)
        {
            using (var client = new HttpClient())
            {
                client.MaxResponseContentBufferSize = 256000;
                client.Timeout = TimeSpan.FromSeconds(30);

                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("email", email)
                });

                HttpResponseMessage response = await client.PostAsync(new StringBuilder(WEB_URL)
                    .Append("/account/forgotpassword").ToString(), content);

                return new Tuple<bool, string>(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            }
        }

        /// <summary>
        /// Encodes an UTF-8 byte array to a Base64URL
        /// </summary>
        /// <param name="array">The UTF-8 byte array to encode.</param>
        /// <returns>The array encoded.</returns>
        private static string EncodeURL64(byte[] array)
        {
            string s = Convert.ToBase64String(array, 0, array.Length);
            s = s.Split(base64PadCharacter)[0]; // Remove any trailing padding
            s = s.Replace(base64Character62, base64UrlCharacter62);  // 62nd char of encoding
            s = s.Replace(base64Character63, _base64UrlCharacter63);  // 63rd char of encoding

            return s;
        }
    }

    /// <summary>
    /// Class used to load the application service Scanner when Android boots up
    /// </summary>
    [BroadcastReceiver]
    [IntentFilter(new[] { Intent.ActionBootCompleted }, Categories = new[] { Intent.CategoryDefault })]
    class BootLoader : BroadcastReceiver
    {
        public override void OnReceive(Context context, Intent intent)
        {
            if (intent.Action != null && intent.Action == Intent.ActionBootCompleted && Utils.UserLogged(context))
            {
                // Start service
                Intent service = new Intent(context, typeof(Scanner));
                service.AddFlags(ActivityFlags.NewTask);
                service.AddFlags(ActivityFlags.FromBackground);
                context.ApplicationContext.StartService(service);
            }
        }
    }
}