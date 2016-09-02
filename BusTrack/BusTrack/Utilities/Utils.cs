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
        public static readonly string PREF_USER_NAME = "userName";
        public static readonly string PREF_USER_EMAIL = "userEmail";
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
        /// Checks if User/Refresh token are valid.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <returns>True or false whether those tokens are still valids or they have expired.</returns>
        public async static Task<bool> CheckLogin(Context context)
        {
            if (!UserLogged(context)) return false;
            // 1-> Check if user token is valid with shared prefs
            ISharedPreferences prefs = context.GetSharedPreferences(NAME_PREF, FileCreationMode.Private);
            if (!prefs.Contains(PREF_VALID_TOKEN)) return false;
            if (prefs.GetLong(PREF_VALID_TOKEN, 0) > ToUnixEpochDate(DateTime.Now)) return true;

            // 2-> Check if user token is valid with web server
            if ((await CallWebAPI("/oauth/check", context)).IsSuccessStatusCode) return true;

            // 3-> User token expired, refresh it!
            if (await Refresh(context)) return true;

            // 4-> Refreh token expired too, request a new login!
            return false;
        }

        /// <summary>
        /// Gets the user statistics from the server.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <returns>A JSON string with the user statistics or an empty one if something went wrong.</returns>
        public async static Task<string> GetStatistics(Context context)
        {
            ISharedPreferences prefs = context.GetSharedPreferences(NAME_PREF, FileCreationMode.Private);
            long id = prefs.GetLong(PREF_USER_ID, -1);

            var content = new[]
            {
                new KeyValuePair<string, string>("id", id.ToString())
            };
            HttpResponseMessage response = await CallWebAPI("/account/getstatistics", context, content);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : string.Empty;
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
            var content = new[]
            {
                new KeyValuePair<string, string>("username", user),
                new KeyValuePair<string, string>("password", PerformClientHash(user, pass))
            };
            HttpResponseMessage response = await CallWebAPI("/oauth/generate", arrayContent: content);
            if (response.IsSuccessStatusCode)
            {
                string token = await response.Content.ReadAsStringAsync();
                var jsonResp = JObject.Parse(token);
                ISharedPreferencesEditor edit = context.GetSharedPreferences(NAME_PREF, FileCreationMode.Private)
                    .Edit();
                edit.PutLong(PREF_USER_ID, jsonResp["id"].ToObject<long>());
                edit.PutLong(PREF_VALID_TOKEN, ToUnixEpochDate(DateTime.Now.AddSeconds(jsonResp["expires_in"].ToObject<double>())));
                edit.PutString(PREF_USER_TOKEN, token);
                edit.PutString(PREF_USER_NAME, jsonResp["name"].ToString());
                edit.PutString(PREF_USER_EMAIL, user);
                edit.Commit();
            }
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Checks if an user is logged.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <returns>True or false depending if an user is logged or not.</returns>
        public static bool UserLogged(Context context)
        {
            ISharedPreferences prefs = context.GetSharedPreferences(NAME_PREF, FileCreationMode.Private);
            bool value = prefs.Contains(PREF_USER_TOKEN);
            if (value)
            {
                var json = JObject.Parse(prefs.GetString(PREF_USER_TOKEN, ""));
                ISharedPreferencesEditor edit = prefs.Edit();
                if (!prefs.Contains(PREF_USER_ID)) edit.PutLong(PREF_USER_ID, json["id"].ToObject<long>());
                if (!prefs.Contains(PREF_VALID_TOKEN)) edit.PutLong(PREF_VALID_TOKEN, ToUnixEpochDate(DateTime.Now.AddSeconds(json["expires_in"].ToObject<double>())));
                if (!prefs.Contains(PREF_USER_NAME)) edit.PutString(PREF_USER_NAME, json["name"].ToString());
                if (!prefs.Contains(PREF_USER_EMAIL)) edit.PutString(PREF_USER_EMAIL, json["email"].ToString());
            }
            return value;
        }

        /// <summary>
        /// Refreshes the user token.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <returns>True or false depending if the action was successful or not.</returns>
        public async static Task<bool> Refresh(Context context)
        {
            ISharedPreferences prefs = context.GetSharedPreferences(NAME_PREF, FileCreationMode.Private);
            string token = prefs.GetString(PREF_USER_TOKEN, "");
            if (token.Length == 0) return false;

            var content = new[]
            {
                new KeyValuePair<string, string>("token", JObject.Parse(token)["access_token"].ToString())
            };
            HttpResponseMessage response = await CallWebAPI("/oauth/refresh", arrayContent: content);
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
                edit.Remove(PREF_USER_EMAIL);
                edit.Remove(PREF_USER_NAME);
                edit.Commit();
                context.StopService(new Intent(context, typeof(Scanner)));
            }
        }

        /// <summary>
        /// Deletes the account from the system.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <param name="sign">The user password to confirm it.</param>
        /// <returns>True or false whether the account has been deleted or not.</returns>
        internal static async Task<bool> DeleteAccount(Context context, string sign)
        {
            ISharedPreferences prefs = context.GetSharedPreferences(NAME_PREF, FileCreationMode.Private);
            long id = prefs.GetLong(PREF_USER_ID, -1);
            var json = JObject.Parse(prefs.GetString(PREF_USER_TOKEN, ""));

            var content = new[]
            {
                new KeyValuePair<string, string>("id", id.ToString()),
                new KeyValuePair<string, string>("sign", PerformClientHash(json["email"].ToString(), sign))
            };
            HttpResponseMessage response = await CallWebAPI("/account/delete", context, content);
            if (response.IsSuccessStatusCode) Logout(context);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Changes the account credentials.
        /// </summary>
        /// <param name="type">The credential type to change.</param>
        /// <param name="data">The credential data to set.</param>
        /// <param name="sign">The password hash to confirm these changes.</param>
        /// <param name="context">Android context.</param>
        /// <returns>True or false whether the changes has been successful or not.</returns>
        internal async static Task<bool> ChangeCredentials(CredentialType type, string data, string sign, Context context)
        {
            ISharedPreferences prefs = context.GetSharedPreferences(NAME_PREF, FileCreationMode.Private);
            long id = prefs.GetLong(PREF_USER_ID, -1);

            var content = new[]
            {
                new KeyValuePair<string, string>("id", id.ToString()),
                new KeyValuePair<string, string>(type.ToString("g"), data),
                new KeyValuePair<string, string>("sign", sign)
            };

            HttpResponseMessage response = await CallWebAPI("/account/change" + type.ToString("g").ToLower(), context, content);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Registers a new user into the system.
        /// </summary>
        /// <param name="name">The user name.</param>
        /// <param name="email">The user email.</param>
        /// <param name="pass">The user password.</param>
        /// <returns>A tuple with:
        /// - Item1: Flag indicating if the registration has been successful.
        /// - Item2: The response content (Only for errors).</returns>
        internal async static Task<Tuple<bool, string>> Register(string name, string email, string pass)
        {
            var content = new[]
            {
                new KeyValuePair<string, string>("name", name),
                new KeyValuePair<string, string>("email", email),
                new KeyValuePair<string, string>("password", PerformClientHash(email, pass))
            };
            HttpResponseMessage response = await CallWebAPI("/oauth/register", arrayContent: content);
            return new Tuple<bool, string>(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// Performs the forgot password action.
        /// </summary>
        /// <param name="email">The user email.</param>
        /// <returns>A tuple with:
        /// - Item1: Flag indicating if the action has been successful.
        /// - Item2: The response content (Only for errors).</returns>
        internal async static Task<Tuple<bool, string>> Forgot(string email)
        {
            var content = new[]
            {
                new KeyValuePair<string, string>("email", email)
            };
            HttpResponseMessage response = await CallWebAPI("/account/forgotpassword", arrayContent: content);
            return new Tuple<bool, string>(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// Performs a hash.
        /// </summary>
        /// <param name="salt">The hash salt.</param>
        /// <param name="toHash">The string to hash.</param>
        /// <returns>The string hashed.</returns>
        internal static string PerformClientHash(string salt, string toHash)
        {
            using (var sha = SHA512.Create())
            {
                string s = EncodeURL64(sha.ComputeHash(Encoding.UTF8.GetBytes(salt)));
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(toHash + s)));
            }
        }

        /// <summary>
        /// Method in charge of performs requests to the web server.
        /// </summary>
        /// <param name="urlPath">The url path (excluding base url).</param>
        /// <param name="context">Android context (default to null). If it's provided, it will be used to retrieve the user token.</param>
        /// <param name="arrayContent">The POST content (default to null). If it's not provided, a GET request will be used instead.</param>
        /// <returns>A HttpResponseMessage object.</returns>
        private async static Task<HttpResponseMessage> CallWebAPI(string urlPath, Context context = null, IEnumerable<KeyValuePair<string, string>> arrayContent = null)
        {
            using (var client = new HttpClient())
            {
                // Setup http client
                client.MaxResponseContentBufferSize = 256000;
                client.Timeout = TimeSpan.FromSeconds(30);

                if (context != null)
                {
                    // Ensure there is an user logged!
                    if (!UserLogged(context)) return new HttpResponseMessage(HttpStatusCode.Unauthorized);

                    ISharedPreferences prefs = context.GetSharedPreferences(NAME_PREF, FileCreationMode.Private);
                    if (!prefs.Contains(PREF_USER_TOKEN)) return new HttpResponseMessage(HttpStatusCode.Unauthorized);

                    // Set token inside authenticator client
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", JObject.Parse(prefs.GetString(PREF_USER_TOKEN, ""))["access_token"].ToString());
                }

                // Build URL
                var url = new StringBuilder(WEB_URL).Append(urlPath).ToString();

                // Use POST or GET
                if (arrayContent != null)
                {
                    var content = new FormUrlEncodedContent(arrayContent);
                    return await client.PostAsync(url, content);
                }
                else return await client.GetAsync(url);
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

    enum CredentialType
    {
        Name,
        Email,
        Password
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