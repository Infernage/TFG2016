using Android.Content;
using Android.Gms.Common;
using Android.Gms.Common.Apis;
using Android.Gms.Location;
using Android.Locations;
using Android.OS;

namespace BusTrack.Utilities
{
    internal class LocationUtility : Java.Lang.Object, GoogleApiClient.IConnectionCallbacks, GoogleApiClient.IOnConnectionFailedListener, Android.Gms.Location.ILocationListener
    {
        private GoogleApiClient clientLocation;

        /// <summary>
        /// Retreives the last known location from GPS/network
        /// </summary>
        public Location LastLocation
        {
            get
            {
                return LocationServices.FusedLocationApi.GetLastLocation(clientLocation);
            }
        }

        public LocationUtility(Context context)
        {
            clientLocation = new GoogleApiClient.Builder(context).AddApi(LocationServices.API).AddConnectionCallbacks(this).AddOnConnectionFailedListener(this).Build();
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
                clientLocation.Disconnect();
            }
        }

        public void OnConnected(Bundle connectionHint)
        {
        }

        public void OnConnectionFailed(ConnectionResult result)
        {
            Connect();
        }

        public void OnConnectionSuspended(int cause)
        {
            Connect();
        }

        public void OnLocationChanged(Location location)
        {
        }
    }
}