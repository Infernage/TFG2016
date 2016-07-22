using System.Collections.Generic;
using Android.Content;
using Android.Net.Wifi;
using System.Threading;
using Android.Net;
using Android.App;

namespace BusTrack.Utilities
{
    class WifiUtility : BroadcastReceiver
    {
        private WifiManager wifi;
        private ConnectivityManager connectivity;
        private NotificationManager notificator;
        private List<ScanResult> results = new List<ScanResult>();
        private AutoResetEvent handle;

        /// <summary>
        /// Gets the ScanResult list from the last scan
        /// </summary>
        public List<ScanResult> Results
        {
            get
            {
                lock (results)
                {
                    return results;
                }
            }
        }

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="context">Context activity</param>
        public WifiUtility(Context context, AutoResetEvent evt)
        {
            handle = evt;
            wifi = context.GetSystemService(Context.WifiService) as WifiManager;
            connectivity = context.GetSystemService(Context.ConnectivityService) as ConnectivityManager;
            notificator = context.GetSystemService(Context.NotificationService) as NotificationManager;
            context.RegisterReceiver(this, new IntentFilter(WifiManager.ScanResultsAvailableAction));
            context.RegisterReceiver(this, new IntentFilter(ConnectivityManager.ConnectivityAction));
        }

        /// <summary>
        /// Starts a scan
        /// </summary>
        public void StartScan()
        {
            wifi.StartScan();
        }

        public override void OnReceive(Context context, Intent intent)
        {
            // Check if WiFi is enabled
            if (!connectivity.ActiveNetworkInfo.IsConnected)
            {
                // Send notification to the user
                Notification.Builder builder = new Notification.Builder(context);
                builder.SetSmallIcon(Android.Resource.Drawable.IcDialogAlert);

                builder.SetContentText("Por favor, vuelve a activar el WiFi.");
                builder.SetContentTitle("WiFi desactivado");

                builder.SetDefaults(NotificationDefaults.Sound | NotificationDefaults.Vibrate);

                Intent opts = new Intent(Android.Provider.Settings.ActionWifiSettings);
                PendingIntent wifiOpts = PendingIntent.GetActivity(context, 0, opts, PendingIntentFlags.OneShot);
                builder.SetContentIntent(wifiOpts);

                if ((int)Android.OS.Build.VERSION.SdkInt >= 21)
                {
                    builder.SetCategory(Notification.CategoryError);
                    builder.SetVisibility(NotificationVisibility.Public);
                }

                Notification notif = builder.Build();
                notificator.Notify(0, notif);
                return;
            }

            // Avoid concurrency issues!
            lock (results)
            {
                results.Clear(); // Clear last results and add new ones
                results.AddRange(wifi.ScanResults);
                handle.Set();
            }
        }
    }
}