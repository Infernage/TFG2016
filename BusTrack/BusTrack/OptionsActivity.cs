using Android.App;
using Android.Content;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using BusTrack.Data;
using BusTrack.Utilities;
using Realms;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BusTrack
{
    [Activity(Label = "Opciones")]
    public class OptionsActivity : Activity
    {
        private volatile bool syncing = false;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            RequestWindowFeature(WindowFeatures.NoTitle);

            SetContentView(Resource.Layout.Options);

            MenuInitializer.InitMenu(this);

            Button clearData = FindViewById<Button>(Resource.Id.clearButton), modAcc = FindViewById<Button>(Resource.Id.modifyAccount),
                delAcc = FindViewById<Button>(Resource.Id.deleteAccount), netDetection = FindViewById<Button>(Resource.Id.detectButton),
                timeTravel = FindViewById<Button>(Resource.Id.timeTravelButton), sendDataButton = FindViewById<Button>(Resource.Id.sendDataButton);
            CheckedTextView sendDataChecker = FindViewById<CheckedTextView>(Resource.Id.sendDataCheck);
            ISharedPreferences prefs = GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private);
            sendDataChecker.Checked = prefs.GetBoolean("autoSync" + prefs.GetLong(Utils.PREF_USER_ID, -1).ToString(), true);
            sendDataButton.Enabled = !sendDataChecker.Checked;

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
                    using (Realm realm = Realm.GetInstance(Utils.GetDB()))
                    {
                        int userId = GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private).GetInt(Utils.PREF_USER_ID, -1);
                        if (userId == -1)
                        {
                            OAuthUtils.Logout(this);
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
                        if (await AccountUtils.DeleteAccount(this, input.Text))
                        {
                            dialog.Dismiss();
                            Finish();
                            StartActivity(typeof(LoginActivity));
                        }
                        else
                        {
                            Toast.MakeText(this, "Fallo al borrar la cuenta", ToastLength.Long).Show();
                        }
                    }
                    catch (Exception ex)
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

            netDetection.Click += (o, e) =>
            {
                FragmentTransaction trans = FragmentManager.BeginTransaction();
                Fragment prev = FragmentManager.FindFragmentByTag("netDetection");
                if (prev != null) trans.Remove(prev);
                trans.AddToBackStack(null);
                new NetDetectionDialog(this).Show(trans, "netDetection");
            };

            timeTravel.Click += (o, e) =>
            {
                FragmentTransaction trans = FragmentManager.BeginTransaction();
                Fragment prev = FragmentManager.FindFragmentByTag("timeTravelDet");
                if (prev != null) trans.Remove(prev);
                trans.AddToBackStack(null);
                new TimeTravelDialog(this).Show(trans, "timeTravelDet");
            };

            sendDataButton.Click += (o, e) =>
            {
                if (syncing) return;
                CancellationTokenSource cts = new CancellationTokenSource();

                // Create a progress dialog meanwhile we retrieve user stats
                ProgressDialog dialog = new ProgressDialog(this);
                dialog.Indeterminate = false;
                dialog.SetProgressStyle(ProgressDialogStyle.Spinner);
                dialog.SetMessage("Sincronizando datos...");
                dialog.SetCancelable(true);
                dialog.CancelEvent += (ob, ev) =>
                {
                    cts.Cancel();
                    Toast.MakeText(this, "Cancelado", ToastLength.Long).Show();
                };
                dialog.Show();

                Task.Run(() => Sync());
            };

            sendDataChecker.Click += (o, e) =>
            {
                sendDataButton.Enabled = sendDataChecker.Checked;
                sendDataChecker.Checked = !sendDataChecker.Checked;
                ISharedPreferencesEditor edit = prefs.Edit();
                edit.PutBoolean("autoSync" + prefs.GetLong(Utils.PREF_USER_ID, -1).ToString(), sendDataChecker.Checked);
                edit.Commit();
                if (sendDataChecker.Checked && !syncing) Task.Run(() => Sync());
            };
        }

        /// <summary>
        /// Uploads all unsynced travels.
        /// </summary>
        /// <param name="cts">A CancellationTokenSource just in the case the user wants to cancel the operation.</param>
        /// <param name="dialog">The progress dialog.</param>
        private void Sync(CancellationTokenSource cts = null, ProgressDialog dialog = null)
        {
            syncing = true;
            using (Realm realm = Realm.GetInstance(Utils.GetDB()))
            {
                var query = realm.All<Travel>().Where(t => t.synced == false);
                int total = query.Count();
                RunOnUiThread(() =>
                {
                    if (dialog != null) dialog.Max = total;
                });

                int progress = 0, failed = 0;

                foreach (Travel travel in query)
                {
                    if (cts.IsCancellationRequested)
                    {
                        if (dialog != null) dialog.Dismiss();
                        goto finish;
                    }
                    if (RestUtils.UploadTravel(this, travel, cts).Result)
                    {
                        realm.Write(() => travel.synced = true);
                        progress++;
                        RunOnUiThread(() =>
                        {
                            if (dialog != null) dialog.Progress = progress;
                        });
                    }
                    else failed++;
                }
                if (dialog != null)
                {
                    RunOnUiThread(() =>
                    {
                        dialog.Dismiss();
                        Toast.MakeText(this, string.Format("Enviados: {0}, Fallidos: {1}, Totales: {2}", progress, failed, total), ToastLength.Long).Show();
                    });
                }
            }

        finish:
            syncing = false;
        }
    }

    internal class TimeTravelDialog : DialogFragment
    {
        private Context context;

        public TimeTravelDialog(Context cont)
        {
            context = cont;
        }

        public override Dialog OnCreateDialog(Bundle savedInstanceState)
        {
            base.OnCreateDialog(savedInstanceState);

            // Create dialog
            AlertDialog dialog;
            var builder = new AlertDialog.Builder(context);
            builder.SetView(Resource.Layout.TimeTravelOpt);
            builder.SetTitle("Tiempo de detección de viaje");
            builder.SetPositiveButton("Aceptar", (EventHandler<DialogClickEventArgs>)null);
            builder.SetNegativeButton("Cancelar", (o, e) => Dismiss());

            dialog = builder.Create();
            dialog.Show();

            TextView text = dialog.FindViewById<TextView>(Resource.Id.textView1);
            SeekBar bar = dialog.FindViewById<SeekBar>(Resource.Id.seekBar1);

            string format = "{0} segundos";
            int time = Utils.GetTimeTravelDetection(context);

            text.Text = string.Format(format, time);
            bar.Progress = time < 3 ? 0 : time - 3;
            bar.Max = 7;
            bar.ProgressChanged += (o, e) =>
            {
                text.Text = string.Format(format, e.Progress + 3);
            };

            Button accept = dialog.GetButton((int)DialogButtonType.Positive);
            accept.Click += (o, e) =>
            {
                Utils.SetTimeTravelDetection(context, bar.Progress + 3);
                dialog.Dismiss();
            };

            return dialog;
        }
    }

    internal class NetDetectionDialog : DialogFragment
    {
        private Context context;

        public NetDetectionDialog(Context cont)
        {
            context = cont;
        }

        public override Dialog OnCreateDialog(Bundle savedInstanceState)
        {
            base.OnCreateDialog(savedInstanceState);

            // Create dialog
            AlertDialog dialog;
            var builder = new AlertDialog.Builder(context);
            builder.SetView(Resource.Layout.NetDetectionOpt);
            builder.SetPositiveButton("Aceptar", (EventHandler<DialogClickEventArgs>)null);
            builder.SetNegativeButton("Cancelar", (o, e) => Dismiss());

            dialog = builder.Create();
            dialog.Show();

            NumberPicker upPicker = dialog.FindViewById<NumberPicker>(Resource.Id.upPicker),
                downPicker = dialog.FindViewById<NumberPicker>(Resource.Id.downPicker);
            Tuple<int, int> stored = Utils.GetNetworkDetection(context);

            // Max value of 80, min value of 30
            upPicker.MaxValue = downPicker.MaxValue = 80;
            upPicker.MinValue = downPicker.MinValue = 30;

            upPicker.Value = stored.Item1;
            downPicker.Value = stored.Item2;

            Button accept = dialog.GetButton((int)DialogButtonType.Positive);
            accept.Click += (o, e) =>
            {
                Utils.SetNetworkDetection(context, new Tuple<int, int>(upPicker.Value, downPicker.Value));
                dialog.Dismiss();
            };

            return dialog;
        }
    }

    internal class AccountModifierDialog : DialogFragment
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
            ISharedPreferences prefs = context.GetSharedPreferences(Utils.NAME_PREF, FileCreationMode.Private);
            EditText nameF = dialog.FindViewById<EditText>(Resource.Id.amNameF),
                emailF = dialog.FindViewById<EditText>(Resource.Id.amEmailF),
                nPassF = dialog.FindViewById<EditText>(Resource.Id.amNewPassF),
                signF = dialog.FindViewById<EditText>(Resource.Id.amSign);

            string sName = prefs.GetString(OAuthUtils.PREF_USER_NAME, "");
            nameF.Text = sName;
            string sEmail = prefs.GetString(OAuthUtils.PREF_USER_EMAIL, "");
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
                            if (await AccountUtils.ChangeCredentials(CredentialType.Name, name, OAuthUtils.PerformClientHash(sEmail, sign), context)) edit.PutString(OAuthUtils.PREF_USER_NAME, name);
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
                            if (!await AccountUtils.ChangeCredentials(CredentialType.Password, OAuthUtils.PerformClientHash(email, sign), OAuthUtils.PerformClientHash(sEmail, sign), context))
                            {
                                Activity.RunOnUiThread(() => Toast.MakeText(context, Resource.String.SyncErr, ToastLength.Long).Show());
                                return;
                            }

                            // If it's not possible to change email, revert password change
                            if (await AccountUtils.ChangeCredentials(CredentialType.Email, email, OAuthUtils.PerformClientHash(email, sign), context)) edit.PutString(OAuthUtils.PREF_USER_EMAIL, email);
                            else
                            {
                                await AccountUtils.ChangeCredentials(CredentialType.Password, OAuthUtils.PerformClientHash(sEmail, sign), OAuthUtils.PerformClientHash(email, sign), context);
                                Activity.RunOnUiThread(() => Toast.MakeText(context, Resource.String.SyncErr, ToastLength.Long).Show());
                                return;
                            }
                        }
                        // If password changed, sync between prefs and server
                        if (pass.Length != 0)
                        {
                            string nPass = OAuthUtils.PerformClientHash(email, pass);
                            pass = string.Empty;
                            if (!await AccountUtils.ChangeCredentials(CredentialType.Password, nPass, OAuthUtils.PerformClientHash(email, sign), context))
                            {
                                Activity.RunOnUiThread(() => Toast.MakeText(context, Resource.String.SyncErr, ToastLength.Long).Show());
                                return;
                            }
                        }
                        edit.Commit();
                        dialog.Dismiss();
                    }
                    catch (Exception ex)
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