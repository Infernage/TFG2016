using Android.App;
using Android.Widget;
using BusTrack.Utilities;
using System;

namespace BusTrack
{
    internal class MenuInitializer
    {
        public static void InitMenu(Activity current)
        {
            // Init menu button
            var menu = current.FindViewById<FlyOutContainer>(Resource.Id.FlyOutContainer);
            EventHandler evt = (sender, e) =>
            {
                menu.AnimatedOpened = !menu.AnimatedOpened;
            };
            current.FindViewById(Resource.Id.MenuButton).Click += evt;

            // Init main menu button
            LinearLayout main = current.FindViewById<LinearLayout>(Resource.Id.mainView);
            main.Click += evt;
            main.Click += (sender, e) =>
            {
                if (!(current is MainActivity))
                {
                    current.StartActivity(typeof(MainActivity));
                    current.Finish();
                }
            };

            // Init recent stats menu button
            LinearLayout stats = current.FindViewById<LinearLayout>(Resource.Id.recentStats);
            stats.Click += evt;
            stats.Click += (sender, e) =>
            {
                if (!(current is StatsActivity))
                {
                    current.StartActivity(typeof(StatsActivity));
                    current.Finish();
                }
            };

            // Init map menu button
            LinearLayout map = current.FindViewById<LinearLayout>(Resource.Id.showMap);
            map.Click += evt;
            map.Click += (sender, e) =>
            {
                if (!(current is MapActivity))
                {
                    current.StartActivity(typeof(MapActivity));
                    current.Finish();
                }
            };

            // Init options menu button
            LinearLayout options = current.FindViewById<LinearLayout>(Resource.Id.showOptions);
            options.Click += evt;
            options.Click += (sender, e) =>
            {
                if (!(current is OptionsActivity))
                {
                    current.StartActivity(typeof(OptionsActivity));
                    current.Finish();
                }
            };

            // Init logout menu button
            LinearLayout logout = current.FindViewById<LinearLayout>(Resource.Id.logout);
            logout.Click += evt;
            logout.Click += (sender, e) =>
            {
                AlertDialog dialog = null;
                var builder = new AlertDialog.Builder(current);
                builder.SetTitle("Desconexión");
                builder.SetMessage("¿Quieres desconectarte?");
                builder.SetPositiveButton("Sí", (o, ev) =>
                {
                    dialog.Dismiss();
                    OAuthUtils.Logout(current);
                    if (!(current is LoginActivity))
                    {
                        current.StartActivity(typeof(LoginActivity));
                        current.Finish();
                    }
                });
                builder.SetNegativeButton("No", (o, ev) =>
                {
                    dialog.Dismiss();
                });

                dialog = builder.Create();
                dialog.Show();
            };
        }
    }
}