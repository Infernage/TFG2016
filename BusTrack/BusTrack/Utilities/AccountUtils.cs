using Android.Content;
using BusTrack.Data;
using Newtonsoft.Json.Linq;
using Realms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BusTrack.Utilities
{
    public class AccountUtils
    {
        /// <summary>
        /// Gets the user statistics from the server.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <param name="ct">The cancellation token used for cancel the operation.</param>
        /// <returns>A JSON string with the user statistics or an empty one if something went wrong.</returns>
        public async static Task<string> GetStatistics(Context context, CancellationToken ct)
        {
            ISharedPreferences prefs = context.GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private);
            long id = prefs.GetLong(Utils.PREF_USER_ID, -1);

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("id", id.ToString())
            });
            HttpResponseMessage response = await RestUtils.CallWebAPI("/account/getstatistics", ct, context, content, bearer: true);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : string.Empty;
        }

        /// <summary>
        /// Deletes the account from the system.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <param name="sign">The user password to confirm it.</param>
        /// <returns>True or false whether the account has been deleted or not.</returns>
        internal static async Task<bool> DeleteAccount(Context context, string sign)
        {
            ISharedPreferences prefs = context.GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private);
            long id = prefs.GetLong(Utils.PREF_USER_ID, -1);
            var json = JObject.Parse(prefs.GetString(OAuthUtils.PREF_USER_TOKEN, ""));

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("id", id.ToString()),
                new KeyValuePair<string, string>("sign", OAuthUtils.PerformClientHash(json["email"].ToString(), sign))
            });
            HttpResponseMessage response = await RestUtils.CallWebAPI("/account/delete", CancellationToken.None, context, content, bearer: true);
            if (response.IsSuccessStatusCode)
            {
                using (Realm realm = Realm.GetInstance(Utils.GetDB()))
                {
                    realm.Write(() => realm.RemoveRange(realm.All<Travel>().Where(t => t.userId == id) as RealmResults<Travel>));
                }
                OAuthUtils.Logout(context);
            }
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
            ISharedPreferences prefs = context.GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private);
            long id = prefs.GetLong(Utils.PREF_USER_ID, -1);

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("id", id.ToString()),
                new KeyValuePair<string, string>(type.ToString("g"), data),
                new KeyValuePair<string, string>("sign", sign)
            });

            HttpResponseMessage response = await RestUtils.CallWebAPI("/account/change" + type.ToString("g").ToLower(), CancellationToken.None, context, content, bearer: true);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Performs the forgot password action.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <param name="email">The user email.</param>
        /// <returns>A tuple with:
        /// - Item1: Flag indicating if the action has been successful.
        /// - Item2: The response content (Only for errors).</returns>
        internal async static Task<Tuple<bool, string>> Forgot(Context context, string email)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("email", email)
            });
            HttpResponseMessage response = await RestUtils.CallWebAPI("/account/forgotpassword", CancellationToken.None, context, content: content);
            return new Tuple<bool, string>(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
    }

    internal enum CredentialType
    {
        Name,
        Email,
        Password
    }
}