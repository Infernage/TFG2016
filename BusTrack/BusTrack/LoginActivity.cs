using System;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using Android.Support.V7.App;
using BusTrack.Utilities;

namespace BusTrack
{
    [Activity(Label = "BusTrack", Theme = "@style/MainTheme")]
    public class LoginActivity : AppCompatActivity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.Login);

            //MenuInitializer.InitMenu(this);

            Button loginB = FindViewById<Button>(Resource.Id.loginButton);
            Button createB = FindViewById<Button>(Resource.Id.createAccButton);
            Button forgotB = FindViewById<Button>(Resource.Id.forgotButton);
            EditText emailF = FindViewById<EditText>(Resource.Id.emailField);
            EditText passF = FindViewById<EditText>(Resource.Id.passField);

            // Login button
            loginB.Click += async (o, e) =>
            {
                loginB.Enabled = false;
                string pswd = passF.Text;
                passF.Text = "";
                string email = emailF.Text;

                if (await Utils.Login(email, pswd, this))
                {
                    StartActivity(typeof(MainActivity));
                    Finish();
                }
                else
                {
                    loginB.Enabled = true;
                }
            };

            // Create account button
            createB.Click += (o, e) =>
            {
                FragmentTransaction trans = FragmentManager.BeginTransaction();
                Fragment prev = FragmentManager.FindFragmentByTag("accountCreator");
                if (prev != null) trans.Remove(prev);
                trans.AddToBackStack(null);
                new CreateAccountDialog(this).Show(trans, "accountCreator");
            };

            // Forgot password button
            forgotB.Click += (o, e) =>
            {
                Android.App.AlertDialog dialog = null;
                Android.App.AlertDialog.Builder builder = new Android.App.AlertDialog.Builder(this);
                builder.SetTitle("Introduzca su correo electrónico");

                EditText input = new EditText(this);
                input.InputType = Android.Text.InputTypes.TextVariationEmailAddress;
                builder.SetView(input);

                builder.SetPositiveButton("Aceptar", (EventHandler<DialogClickEventArgs>)null);

                builder.SetNegativeButton("Cancelar", (ob, ev) =>
                {
                    dialog.Dismiss();
                });

                dialog = builder.Create();
                dialog.Show();

                Button accept = dialog.GetButton((int)DialogButtonType.Positive);
                accept.Click += async (ob, ev) =>
                {
                    string email = input.Text;

                    if (string.IsNullOrWhiteSpace(email))
                    {
                        Toast.MakeText(this, "Introduce un correo", ToastLength.Long).Show();
                        return;
                    } else if (!email.Contains("@"))
                    {
                        Toast.MakeText(this, "Introduce un correo válido", ToastLength.Long).Show();
                        return;
                    }

                    Tuple<bool, string> response = await Utils.Forgot(email, this);
                    if (response.Item1)
                    {
                        Toast.MakeText(this, "Se ha enviado un correo a la dirección especificada", ToastLength.Long).Show();
                        dialog.Dismiss();
                    }
                    else
                    {
                        Toast.MakeText(this, response.Item2, ToastLength.Long).Show();
                    }
                };
            };
        }
    }

    /// <summary>
    /// Class used as dialog for account creation.
    /// </summary>
    class CreateAccountDialog : DialogFragment
    {
        private Context context;

        public CreateAccountDialog(Context context)
        {
            this.context = context;
        }

        public override Dialog OnCreateDialog(Bundle savedInstanceState)
        {
            Android.App.AlertDialog dialog = null;
            var builder = new Android.App.AlertDialog.Builder(context);
            builder.SetView(Resource.Layout.AccountCreator);
            builder.SetMessage("Introduce las credenciales");
            builder.SetPositiveButton("Aceptar", (EventHandler<DialogClickEventArgs>) null);

            dialog = builder.Create();

            dialog.Show();

            Button accept = dialog.GetButton((int)DialogButtonType.Positive);
            accept.Click += async (o, e) =>
            {
                string name = dialog.FindViewById<EditText>(Resource.Id.acNameF).Text,
                    email = dialog.FindViewById<EditText>(Resource.Id.acEmailF).Text,
                    pass = dialog.FindViewById<EditText>(Resource.Id.acPassF).Text;

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email)
                    || string.IsNullOrWhiteSpace(pass))
                {
                    Toast.MakeText(context, "Debes incluir todas las credenciales", ToastLength.Long).Show();
                    return;
                }
                else if (!email.Contains("@"))
                {
                    Toast.MakeText(context, "Introduce un correo válido", ToastLength.Long).Show();
                    return;
                }
                else if (pass.Length <= 4)
                {
                    Toast.MakeText(context, "La contraseña debe tener al menos 5 caracteres", ToastLength.Long).Show();
                    return;
                }

                Tuple<bool, string> response = await Utils.Register(name, email, pass, context);
                if (response.Item1) Dismiss();
                else
                {
                    Toast.MakeText(context, response.Item2, ToastLength.Long).Show();
                }
            };

            return dialog;
        }
    }
}