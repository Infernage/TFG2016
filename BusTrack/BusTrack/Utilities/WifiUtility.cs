using Android.Content;
using Android.Net.Wifi;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BusTrack.Utilities
{
    internal class WifiUtility : BroadcastReceiver
    {
        public delegate void UpdateEventHandler(List<ScanResult> networks);

        public static event UpdateEventHandler UpdateNetworks;

        private WifiManager wifi;
        private List<ScanResult> results = new List<ScanResult>();
        private AutoResetEvent handle;
        private Context mContext;

        /// <summary>
        /// Gets the ScanResult list from the last scan
        /// </summary>
        public List<ScanResult> Results
        {
            get
            {
                lock (results)
                {
                    return new List<ScanResult>(results);
                }
            }
        }

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="context">Context activity</param>
        public WifiUtility(Context context, AutoResetEvent evt)
        {
            mContext = context;
            handle = evt;
            wifi = context.GetSystemService(Context.WifiService) as WifiManager;
            context.RegisterReceiver(this, new IntentFilter(WifiManager.ScanResultsAvailableAction));
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
            // Avoid concurrency issues!
            lock (results)
            {
                results.Clear(); // Clear last results and add new ones
                results.AddRange(wifi.ScanResults);
                handle.Set();
            }

            // Notify throughout delegate
            /*
             * This is the same as:
             UpdateEventHandler handler = UpdateNetworks;
             if (handler != null) handler(results);
             */
            Task.Run(() => UpdateNetworks?.Invoke(results));
        }
    }
}