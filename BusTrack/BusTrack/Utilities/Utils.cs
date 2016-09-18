using Android.App;
using Android.Content;
using Newtonsoft.Json;
using Realms;
using System;
using System.Collections.Generic;

namespace BusTrack.Utilities
{
    public class Utils
    {
        public static readonly string PREF_USER_ID = "userID";
        public static readonly string NAME_PREF = "BusTrack";

        public static readonly string NAME_LCHOOSER = "LineChooser";
        public static readonly string NAME_LCREATOR = "LineCreator";

        internal static long ToUnixEpochDate(DateTime date) => (long)Math.Round((date.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);

        /// <summary>
        /// Helper method to get the time travel detection.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <returns>The time to detect a new travel (in seconds).</returns>
        public static int GetTimeTravelDetection(Context context)
        {
            if (!OAuthUtils.UserLogged(context)) return 5;

            ISharedPreferences prefs = context.GetSharedPreferences(NAME_PREF, FileCreationMode.Private);
            return prefs.GetInt("timeTravelDet" + prefs.GetLong(PREF_USER_ID, -1).ToString(), 5);
        }

        /// <summary>
        /// Helper method to set the time travel detection.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <param name="time">The time to detect a new travel (in seconds).</param>
        public static void SetTimeTravelDetection(Context context, int time)
        {
            if (!OAuthUtils.UserLogged(context)) return;

            ISharedPreferences prefs = context.GetSharedPreferences(NAME_PREF, FileCreationMode.Private);
            ISharedPreferencesEditor edit = prefs.Edit();
            edit.PutInt("timeTravelDet" + prefs.GetLong(PREF_USER_ID, -1).ToString(), time >= 3 && time <= 10 ? time : 5);
            edit.Commit();
        }

        /// <summary>
        /// Helper method to get the network intensity which is used to detect a new travel.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <returns>A tuple with:
        /// - Item1: The network intensity used to detect the start of a trip.
        /// - Item2: The network intensity used to detect the finish of a trip.</returns>
        public static Tuple<int, int> GetNetworkDetection(Context context)
        {
            if (!OAuthUtils.UserLogged(context)) return new Tuple<int, int>(50, 70);

            ISharedPreferences prefs = context.GetSharedPreferences(NAME_PREF, FileCreationMode.Private);
            return new Tuple<int, int>(Math.Abs(prefs.GetInt("upNetInt" + prefs.GetLong(PREF_USER_ID, -1).ToString(), -50)),
                Math.Abs(prefs.GetInt("downNetInt" + prefs.GetLong(PREF_USER_ID, -1).ToString(), -70)));
        }

        /// <summary>
        /// Helper method to set the network intensity which is used to detect a new travel.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <param name="tuple">A tuple with:
        /// - Item1: The network intensity used to detect the start of a trip.
        /// - Item2: The network intensity used to detect the finish of a trip.</param>
        public static void SetNetworkDetection(Context context, Tuple<int, int> tuple)
        {
            if (!OAuthUtils.UserLogged(context)) return;

            ISharedPreferences prefs = context.GetSharedPreferences(NAME_PREF, FileCreationMode.Private);
            ISharedPreferencesEditor edit = prefs.Edit();
            int up = tuple.Item1 * -1, down = tuple.Item2 * -1;
            edit.PutInt("upNetInt" + prefs.GetLong(PREF_USER_ID, -1).ToString(), up >= -80 && up <= -30 ? up : -50);
            edit.PutInt("downNetInt" + prefs.GetLong(PREF_USER_ID, -1).ToString(), down >= -80 && down <= -30 ? down : -70);
            edit.Commit();
        }

        /// <summary>
        /// Helper method to get user networks.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <returns>A list with all stored user networks.</returns>
        public static List<string> GetNetworks(Context context)
        {
            if (!OAuthUtils.UserLogged(context)) return new List<string>();

            ISharedPreferences prefs = context.GetSharedPreferences(NAME_PREF, FileCreationMode.Private);
            return JsonConvert.DeserializeObject<List<string>>(prefs.GetString("networks" + prefs.GetLong(PREF_USER_ID, -1).ToString(), "[]"));
        }

        /// <summary>
        /// Helper method to set user networks.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <param name="networks">The new list with user networks.</param>
        public static void SetNetworks(Context context, List<string> networks)
        {
            if (!OAuthUtils.UserLogged(context)) return;

            ISharedPreferences prefs = context.GetSharedPreferences(NAME_PREF, FileCreationMode.Private);
            ISharedPreferencesEditor edit = prefs.Edit();
            edit.PutString("networks" + prefs.GetLong(PREF_USER_ID, -1).ToString(), JsonConvert.SerializeObject(networks, Formatting.Indented));
            edit.Commit();
        }

        /// <summary>
        /// Gets a DB configuration object.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <returns>The DB configuration object.</returns>
        internal static RealmConfiguration GetDB()
        {
            RealmConfiguration config = new RealmConfiguration("BusTrack.realm", true);
            config.SchemaVersion = 1;
            return config;
        }
    }

    /// <summary>
    /// Class used to load the application service Scanner when Android boots up
    /// </summary>
    [BroadcastReceiver]
    [IntentFilter(new[] { Intent.ActionBootCompleted }, Categories = new[] { Intent.CategoryDefault })]
    internal class BootLoader : BroadcastReceiver
    {
        public override void OnReceive(Context context, Intent intent)
        {
            if (intent.Action != null && intent.Action == Intent.ActionBootCompleted && OAuthUtils.UserLogged(context))
            {
                // Start service
                Intent service = new Intent(context, typeof(ScannerService));
                service.AddFlags(ActivityFlags.NewTask);
                service.AddFlags(ActivityFlags.FromBackground);
                context.ApplicationContext.StartService(service);
            }
        }
    }
}