using Android.Content;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BusTrack.Utilities
{
    public class OAuthUtils
    {
        public static readonly string PREF_USER_TOKEN = "userTk";
        public static readonly string PREF_USER_NAME = "userName";
        public static readonly string PREF_USER_EMAIL = "userEmail";

        private static readonly string PREF_VALID_TOKEN = "validTo";

        private static char base64PadCharacter = '=';
        private static char base64Character62 = '+';
        private static char base64Character63 = '/';
        private static char base64UrlCharacter62 = '-';
        private static char base64UrlCharacter63 = '_';

        /// <summary>
        /// Performs a login.
        /// </summary>
        /// <param name="user">User email.</param>
        /// <param name="pass">User password.</param>
        /// <param name="context">Android context.</param>
        /// <param name="ct">The cancellation token used for cancel the operation.</param>
        /// <returns>True or false if response is successful.</returns>
        public async static Task<bool> Login(string user, string pass, Context context, CancellationToken ct)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", user),
                new KeyValuePair<string, string>("password", PerformClientHash(user, pass))
            });
            HttpResponseMessage response = await RestUtils.CallWebAPI("/oauth/generate", ct, context, content: content);
            if (response.IsSuccessStatusCode)
            {
                string token = await response.Content.ReadAsStringAsync();
                var jsonResp = JObject.Parse(token);
                ISharedPreferencesEditor edit = context.GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private)
                    .Edit();
                edit.PutLong(Utils.PREF_USER_ID, jsonResp["id"].ToObject<long>());
                edit.PutLong(PREF_VALID_TOKEN, Utils.ToUnixEpochDate(DateTime.Now.AddSeconds(jsonResp["expires_in"].ToObject<double>())));
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
            ISharedPreferences prefs = context.GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private);
            bool value = prefs.Contains(PREF_USER_TOKEN);
            if (value)
            {
                var json = JObject.Parse(prefs.GetString(PREF_USER_TOKEN, ""));
                ISharedPreferencesEditor edit = prefs.Edit();
                if (!prefs.Contains(Utils.PREF_USER_ID)) edit.PutLong(Utils.PREF_USER_ID, json["id"].ToObject<long>());
                if (!prefs.Contains(PREF_VALID_TOKEN)) edit.PutLong(PREF_VALID_TOKEN, Utils.ToUnixEpochDate(DateTime.Now.AddSeconds(json["expires_in"].ToObject<double>())));
                if (!prefs.Contains(PREF_USER_NAME)) edit.PutString(PREF_USER_NAME, json["name"].ToString());
                if (!prefs.Contains(PREF_USER_EMAIL)) edit.PutString(PREF_USER_EMAIL, json["email"].ToString());
            }
            return value;
        }

        /// <summary>
        /// Logouts the current user.
        /// </summary>
        /// <param name="context">Android context.</param>
        public static void Logout(Context context)
        {
            if (UserLogged(context))
            {
                ISharedPreferencesEditor edit = context.GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private).Edit();
                edit.Remove(Utils.PREF_USER_ID);
                edit.Remove(PREF_USER_TOKEN);
                edit.Remove(PREF_USER_EMAIL);
                edit.Remove(PREF_USER_NAME);
                edit.Commit();
                context.StopService(new Intent(context, typeof(ScannerService)));
            }
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
            ISharedPreferences prefs = context.GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private);
            if (!prefs.Contains(PREF_VALID_TOKEN)) return false;
            if (prefs.GetLong(PREF_VALID_TOKEN, 0) > Utils.ToUnixEpochDate(DateTime.Now)) return true;

            // 2-> Check if user token is valid with web server
            if ((await RestUtils.CallWebAPI("/oauth/check", CancellationToken.None, context, checkLogin: false, bearer: true)).IsSuccessStatusCode) return true;

            // 3-> User token expired, refresh it!
            if (await Refresh(context)) return true;

            // 4-> Refreh token expired too, request a new login!
            Logout(context);
            return false;
        }

        /// <summary>
        /// Registers a new user into the system.
        /// </summary>
        /// <param name="contextm">Android context.</param>
        /// <param name="name">The user name.</param>
        /// <param name="email">The user email.</param>
        /// <param name="pass">The user password.</param>
        /// <returns>A tuple with:
        /// - Item1: Flag indicating if the registration has been successful.
        /// - Item2: The response content (Only for errors).</returns>
        internal async static Task<Tuple<bool, string>> Register(Context context, string name, string email, string pass)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("name", name),
                new KeyValuePair<string, string>("email", email),
                new KeyValuePair<string, string>("password", PerformClientHash(email, pass))
            });
            HttpResponseMessage response = await RestUtils.CallWebAPI("/oauth/register", CancellationToken.None, context, content: content);
            return new Tuple<bool, string>(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// Refreshes the user token.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <returns>True or false depending if the action was successful or not.</returns>
        private async static Task<bool> Refresh(Context context)
        {
            ISharedPreferences prefs = context.GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private);
            string token = prefs.GetString(PREF_USER_TOKEN, "");
            if (token.Length == 0) return false;

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("token", JObject.Parse(token)["refresh_token"].ToString())
            });
            HttpResponseMessage response = await RestUtils.CallWebAPI("/oauth/refresh", CancellationToken.None, context, content: content, checkLogin: false);
            if (response.IsSuccessStatusCode)
            {
                token = await response.Content.ReadAsStringAsync();
                var jsonResp = JObject.Parse(token);
                ISharedPreferencesEditor edit = prefs.Edit();
                edit.PutString(PREF_USER_TOKEN, token);
                edit.PutLong(PREF_VALID_TOKEN, Utils.ToUnixEpochDate(DateTime.Now.AddSeconds(jsonResp["expires_in"].ToObject<double>())));
                edit.Commit();
            }
            return response.IsSuccessStatusCode;
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
        /// Encodes an UTF-8 byte array to a Base64URL
        /// </summary>
        /// <param name="array">The UTF-8 byte array to encode.</param>
        /// <returns>The array encoded.</returns>
        private static string EncodeURL64(byte[] array)
        {
            string s = Convert.ToBase64String(array, 0, array.Length);
            s = s.Split(base64PadCharacter)[0]; // Remove any trailing padding
            s = s.Replace(base64Character62, base64UrlCharacter62);  // 62nd char of encoding
            s = s.Replace(base64Character63, base64UrlCharacter63);  // 63rd char of encoding

            return s;
        }
    }
}