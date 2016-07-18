using Android.App;
using Android.Gms.Maps;
using Android.Gms.Maps.Model;
using Android.OS;
using Android.Views;

namespace BusTrack
{
    [Activity(Label = "Mapa")]
    public class MapActivity : Activity, IOnMapReadyCallback
    {
        public void OnMapReady(GoogleMap googleMap)
        {
            googleMap.MoveCamera(CameraUpdateFactory.NewLatLngZoom(new LatLng(36.7212487, -4.4213463), 15));
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            RequestWindowFeature(WindowFeatures.NoTitle);
            //ActionBar.SetBackgroundDrawable(new ColorDrawable(Color.LightBlue));

            SetContentView(Resource.Layout.Map);

            MenuInitializer.InitMenu(this);

            MapFragment mapFrag = FragmentManager.FindFragmentById<MapFragment>(Resource.Id.map);
            mapFrag.GetMapAsync(this);
        }
    }
}