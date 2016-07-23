using Android.Content;
using Android.OS;
using Android.Gms.Common.Apis;
using Android.Gms.Common;
using Android.Gms.Location;
using Android.Locations;
using System.Threading;

namespace BusTrack.Utilities
{
    class LocationUtility : Java.Lang.Object, GoogleApiClient.IConnectionCallbacks, GoogleApiClient.IOnConnectionFailedListener, Android.Gms.Location.ILocationListener
    {
        private GoogleApiClient clientLocation;
        private Location last;
        //private AutoResetEvent evt;
        
        /// <summary>
        /// Retreives the last known location from GPS/network
        /// </summary>
        public Location LastLocation
        {
            get
            {
                //return last;
                return LocationServices.FusedLocationApi.GetLastLocation(clientLocation);
            }
        }

        public LocationUtility(Context context)
        {
            clientLocation = new GoogleApiClient.Builder(context).AddApi(LocationServices.API).AddConnectionCallbacks(this).AddOnConnectionFailedListener(this).Build();
            //this.evt = evt;
            Connect();
        }

        /// <summary>
        /// Connects to the GoogleApiClient
        /// </summary>
        public void Connect()
        {
            if (!clientLocation.IsConnected) clientLocation.Connect();
        }
    
        /// <summary>
        /// Disconnects from the GoogleApiClient
        /// </summary>
        public void Disconnect()
        {
            if (clientLocation.IsConnected)
            {
                //LocationServices.FusedLocationApi.RemoveLocationUpdates(clientLocation, this);
                clientLocation.Disconnect();
            }
        }

        public void OnConnected(Bundle connectionHint)
        {
            /*LocationRequest req = new LocationRequest();
            req.SetPriority(LocationRequest.PriorityBalancedPowerAccuracy);
            req.SetInterval(5000);
            req.SetFastestInterval(5000);
            LocationServices.FusedLocationApi.RequestLocationUpdates(clientLocation, req, this);*/
        }

        public void OnConnectionFailed(ConnectionResult result)
        {
            if (result.ErrorCode == ConnectionResult.ServiceDisabled || result.ErrorCode == ConnectionResult.ServiceInvalid || result.ErrorCode == ConnectionResult.ServiceMissing)
            {
                // TODO: Notify user to install/activate Google Play services
            }
        }

        public void OnConnectionSuspended(int cause)
        {
            if (cause == GoogleApiClient.ConnectionCallbacks.CauseServiceDisconnected)
            {
                // TODO: Notify user to re-enable GPS
            }
        }

        public void OnLocationChanged(Location location)
        {
            last = location;
        }
    }
}