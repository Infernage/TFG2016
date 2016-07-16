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
    [Activity(Label = "OptionsActivity")]
    public class OptionsActivity : Activity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            RequestWindowFeature(WindowFeatures.NoTitle);
            //ActionBar.SetBackgroundDrawable(new ColorDrawable(Color.LightBlue));

            SetContentView(Resource.Layout.Options);

            MenuInitializer.InitMenu(this);

            Button limitSize = FindViewById<Button>(Resource.Id.dataLimit), clearData = FindViewById<Button>(Resource.Id.clearButton),
                modAcc = FindViewById<Button>(Resource.Id.modifyAccount), delAcc = FindViewById<Button>(Resource.Id.deleteAccount);
            limitSize.Click += (o, e) =>
            {
                FragmentTransaction trans = FragmentManager.BeginTransaction();
                Fragment prev = FragmentManager.FindFragmentByTag("limitData");
                if (prev != null) trans.Remove(prev);
                trans.AddToBackStack(null);
                new LimitDialog().Show(trans, "limitData");
            };

            clearData.Click += (o, e) =>
            {
                // TODO
            };

            modAcc.Click += (o, e) =>
            {
                // TODO
            };

            delAcc.Click += (o, e) =>
            {
                // TODO
            };
        }
    }

    class LimitDialog : DialogFragment
    {
        public override Dialog OnCreateDialog(Bundle savedInstanceState)
        {
            base.OnCreateDialog(savedInstanceState);

            ISharedPreferences prefs = Activity.GetSharedPreferences("user", FileCreationMode.Private);
            NumberPicker picker = new NumberPicker(Activity);
            picker.MaxValue = 100;
            picker.MinValue = 0;
            picker.Value = prefs.GetInt("limitData", 20);

            var builder = new AlertDialog.Builder(Activity);
            builder.SetView(picker);
            builder.SetMessage("Máximo límite de datos (en MB)");
            builder.SetPositiveButton("Aceptar", (o, e) =>
            {
                ISharedPreferencesEditor editor = prefs.Edit();
                editor.PutInt("limitData", picker.Value);
                editor.Apply();
                Dismiss();
            });
            builder.SetNegativeButton("Cancelar", (o, e) =>
            {
                Dismiss();
            });

            return builder.Create();
        }
    }
}