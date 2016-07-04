using System;
using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;
using Android.Graphics.Drawables;
using Android.Graphics;
using Android.Util;

namespace BusTrack
{
    [Activity(Label = "BusTrack", MainLauncher = true, Icon = "@drawable/icon")]
    public class MainActivity : Activity
    {

        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);
            RequestWindowFeature(WindowFeatures.NoTitle);
            //ActionBar.SetBackgroundDrawable(new ColorDrawable(Color.LightBlue));

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.Main);

            MenuInitializer.InitMenu(this);
        }
    }
}

