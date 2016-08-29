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
using Realms;
using BusTrack.Utilities;
using BusTrack.Data;

namespace BusTrack
{
    [Activity(Label = "Opciones")]
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
                Fragment prev = FragmentManager.FindFragmentByTag(Utils.PREF_DATA_LIMIT);
                if (prev != null) trans.Remove(prev);
                trans.AddToBackStack(null);
                new LimitDialog().Show(trans, Utils.PREF_DATA_LIMIT);
            };

            clearData.Click += (o, e) =>
            {
                AlertDialog dialog = null;
                AlertDialog.Builder alert = new AlertDialog.Builder(this);
                alert.SetTitle("Borrar datos");
                alert.SetMessage("¿Seguro de que quieres borrar los datos del dispositivo? (Los datos almacenados en el servidor permanecerán intactos)");
                alert.SetNegativeButton("Cancelar", (ob, ev) =>
                {
                    dialog.Dismiss();
                });
                alert.SetPositiveButton("Aceptar", (ob, ev) =>
                {
                    using (Realm realm = Realm.GetInstance(Utils.NAME_PREF))
                    {
                        int userId = GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private).GetInt(Utils.PREF_USER_ID, -1);
                        if (userId == -1)
                        {
                            // TODO: Logout, cause no user has logged in
                            Toast.MakeText(this, "Error: ID de usuario no encontrada", ToastLength.Long).Show();
                            dialog.Dismiss();
                            return;
                        }

                        var travels = from t in realm.All<Travel>() where t.userId == userId select t;
                        realm.Write(() =>
                        {
                            realm.RemoveRange(travels as RealmResults<Travel>);
                        });
                    }
                    dialog.Dismiss();
                });
                dialog = alert.Create();
                dialog.Show();
            };

            modAcc.Click += (o, e) =>
            {
                // TODO: Create UI
            };

            delAcc.Click += (o, e) =>
            {
                // TODO: Create option (Depends on server)
            };
        }
    }

    class LimitDialog : DialogFragment
    {
        public override Dialog OnCreateDialog(Bundle savedInstanceState)
        {
            base.OnCreateDialog(savedInstanceState);

            ISharedPreferences prefs = Activity.GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private);
            NumberPicker picker = new NumberPicker(Activity);
            picker.MaxValue = 100;
            picker.MinValue = 0;
            picker.Value = prefs.GetInt(Utils.PREF_DATA_LIMIT, 20);

            var builder = new AlertDialog.Builder(Activity);
            builder.SetView(picker);
            builder.SetMessage("Máximo límite de datos (en MB)");
            builder.SetPositiveButton("Aceptar", (o, e) =>
            {
                ISharedPreferencesEditor editor = prefs.Edit();
                editor.PutInt(Utils.PREF_DATA_LIMIT, picker.Value);
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