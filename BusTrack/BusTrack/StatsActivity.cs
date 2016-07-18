using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;

namespace BusTrack
{
    [Activity(Label = "Estadísticas")]
    public class StatsActivity : Activity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            RequestWindowFeature(WindowFeatures.NoTitle);
            //ActionBar.SetBackgroundDrawable(new ColorDrawable(Color.LightBlue));

            SetContentView(Resource.Layout.Stats);

            MenuInitializer.InitMenu(this);
        }
    }
}