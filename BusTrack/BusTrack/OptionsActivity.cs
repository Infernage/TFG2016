using System;
using System.Linq;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Realms;
using BusTrack.Utilities;
using BusTrack.Data;
using System.Threading.Tasks;
using Android.Util;

namespace BusTrack
{
    [Activity(Label = "Opciones")]
    public class OptionsActivity : Activity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            RequestWindowFeature(WindowFeatures.NoTitle);

            SetContentView(Resource.Layout.Options);

            MenuInitializer.InitMenu(this);

            Button clearData = FindViewById<Button>(Resource.Id.clearButton),
                modAcc = FindViewById<Button>(Resource.Id.modifyAccount), delAcc = FindViewById<Button>(Resource.Id.deleteAccount);

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
                    using (Realm realm = Realm.GetInstance(Utils.GetDB(this)))
                    {
                        int userId = GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private).GetInt(Utils.PREF_USER_ID, -1);
                        if (userId == -1)
                        {
                            Utils.Logout(this);
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
                FragmentTransaction trans = FragmentManager.BeginTransaction();
                Fragment prev = FragmentManager.FindFragmentByTag("accountModifier");
                if (prev != null) trans.Remove(prev);
                trans.AddToBackStack(null);
                new AccountModifierDialog(this).Show(trans, "accountModifier");
            };

            delAcc.Click += (o, e) =>
            {
                AlertDialog dialog = null;
                var builder = new AlertDialog.Builder(this);
                builder.SetTitle("Confirma el borrado con tu contraseña");

                EditText input = new EditText(this);
                input.InputType = Android.Text.InputTypes.TextVariationPassword | Android.Text.InputTypes.ClassText;
                builder.SetView(input);
                builder.SetPositiveButton("Aceptar", async (ob, ev) =>
                {
                    try
                    {
                        if (await Utils.DeleteAccount(this, input.Text))
                        {
                            dialog.Dismiss();
                            Finish();
                            StartActivity(typeof(LoginActivity));
                        }
                        else
                        {
                            Toast.MakeText(this, "Fallo al borrar la cuenta", ToastLength.Long).Show();
                        }
                    } catch (Exception ex)
                    {
                        Log.Error(Utils.NAME_PREF, Java.Lang.Throwable.FromException(ex), "Delete failed!");
                        RunOnUiThread(() => Toast.MakeText(this, Resource.String.SesExp, ToastLength.Long).Show());
                        dialog.Dismiss();
                        Finish();
                        StartActivity(typeof(LoginActivity));
                    }
                });
                builder.SetNegativeButton("Cancelar", (ob, ev) => dialog.Dismiss());

                dialog = builder.Create();
                dialog.Show();
            };
        }
    }

    class AccountModifierDialog : DialogFragment
    {
        private Context context;

        public AccountModifierDialog(Context cont)
        {
            context = cont;
        }

        public override Dialog OnCreateDialog(Bundle savedInstanceState)
        {
            base.OnCreateDialog(savedInstanceState);

            // Create dialog
            AlertDialog dialog;
            var builder = new AlertDialog.Builder(context);
            builder.SetView(Resource.Layout.AccountModifier);
            builder.SetPositiveButton("Aceptar", (EventHandler<DialogClickEventArgs>)null);
            builder.SetNegativeButton("Cancelar", (o, e) => Dismiss());

            dialog = builder.Create();
            dialog.Show();

            // Get all data & variables
            ISharedPreferences prefs = Activity.GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private);
            EditText nameF = dialog.FindViewById<EditText>(Resource.Id.amNameF),
                emailF = dialog.FindViewById<EditText>(Resource.Id.amEmailF),
                nPassF = dialog.FindViewById<EditText>(Resource.Id.amNewPassF),
                signF = dialog.FindViewById<EditText>(Resource.Id.amSign);

            string sName = prefs.GetString(Utils.PREF_USER_NAME, "");
            nameF.Text = sName;
            string sEmail = prefs.GetString(Utils.PREF_USER_EMAIL, "");
            emailF.Text = sEmail;

            Button accept = dialog.GetButton((int)DialogButtonType.Positive);
            accept.Click += async (o, e) =>
            {
                // Need to confirm password in API calls!
                if (signF.Text.Length == 0)
                {
                    Toast.MakeText(context, "Necesitas confirmar la contraseña", ToastLength.Long).Show();
                    return;
                }

                string name = nameF.Text,
                    email = emailF.Text,
                    pass = nPassF.Text,
                    sign = signF.Text;
                signF.Text = string.Empty;
                nPassF.Text = string.Empty;

                await Task.Run(async () =>
                {
                    try
                    {
                        ISharedPreferencesEditor edit = prefs.Edit();
                        // If name changed, sync between prefs and server
                        if (!sName.Equals(name))
                        {
                            if (await Utils.ChangeCredentials(CredentialType.Name, name, Utils.PerformClientHash(sEmail, sign), context)) edit.PutString(Utils.PREF_USER_NAME, name);
                            else
                            {
                                // If something went wrong, tell user and abort
                                Activity.RunOnUiThread(() => Toast.MakeText(context, Resource.String.SyncErr, ToastLength.Long).Show());
                                return;
                            }
                        }
                        // If email changed, sync between prefs and server
                        if (!sEmail.Equals(email))
                        {
                            // If something went wrong, tell user and abort
                            if (!await Utils.ChangeCredentials(CredentialType.Password, Utils.PerformClientHash(email, sign), Utils.PerformClientHash(sEmail, sign), context))
                            {
                                Activity.RunOnUiThread(() => Toast.MakeText(context, Resource.String.SyncErr, ToastLength.Long).Show());
                                return;
                            }

                            // If it's not possible to change email, revert password change
                            if (await Utils.ChangeCredentials(CredentialType.Email, email, Utils.PerformClientHash(email, sign), context)) edit.PutString(Utils.PREF_USER_EMAIL, email);
                            else
                            {
                                await Utils.ChangeCredentials(CredentialType.Password, Utils.PerformClientHash(sEmail, sign), Utils.PerformClientHash(email, sign), context);
                                Activity.RunOnUiThread(() => Toast.MakeText(context, Resource.String.SyncErr, ToastLength.Long).Show());
                                return;
                            }
                        }
                        // If password changed, sync between prefs and server
                        if (pass.Length != 0)
                        {
                            string nPass = Utils.PerformClientHash(email, pass);
                            pass = string.Empty;
                            if (!await Utils.ChangeCredentials(CredentialType.Password, nPass, Utils.PerformClientHash(email, sign), context))
                            {
                                Activity.RunOnUiThread(() => Toast.MakeText(context, Resource.String.SyncErr, ToastLength.Long).Show());
                                return;
                            }
                        }
                        edit.Commit();
                        dialog.Dismiss();
                    } catch (Exception ex)
                    {
                        Log.Error(Utils.NAME_PREF, Java.Lang.Throwable.FromException(ex), "Change credentials failed!");
                        Activity.RunOnUiThread(() =>
                        {
                            Toast.MakeText(context, Resource.String.SesExp, ToastLength.Long).Show();
                        });
                        dialog.Dismiss();
                        Activity.StartActivity(typeof(LoginActivity));
                        Activity.Finish();
                    }
                });
            };

            return dialog;
        }
    }
}