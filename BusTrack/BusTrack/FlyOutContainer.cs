using Android.Animation;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using System;

namespace BusTrack
{
    public class FlyOutContainer : FrameLayout
    {
        private bool opened;
        private int contentOffsetX;
        private ValueAnimator animator;
        private ITimeInterpolator interpolator = new SmoothInterpolator();
        private VelocityTracker velocityTracker;
        private bool stateBeforeTracking;
        private bool isTracking;
        private bool preTracking;
        private int startX = -1, startY = -1;

        private const int BezelArea = 30; //dip
        private const int MaxOverlayAlpha = 170;
        private const float ParallaxSpeedRatio = 0.25f;

        private int touchSlop;
        private int pagingTouchSlop;
        private int minFlingVelocity;
        private int maxFlingVelocity;

        private GradientDrawable shadowDrawable;
        private Paint overlayPaint;

        public FlyOutContainer(Context context) :
            base(context)
        {
            Initialize();
        }

        public FlyOutContainer(Context context, IAttributeSet attrs) :
            base(context, attrs)
        {
            Initialize();
        }

        public FlyOutContainer(Context context, IAttributeSet attrs, int defStyle) :
            base(context, attrs, defStyle)
        {
            Initialize();
        }

        private void Initialize()
        {
            var config = ViewConfiguration.Get(Context);
            touchSlop = config.ScaledTouchSlop;
            pagingTouchSlop = config.ScaledPagingTouchSlop;
            minFlingVelocity = config.ScaledMinimumFlingVelocity;
            maxFlingVelocity = config.ScaledMaximumFlingVelocity;
            const int BaseShadowColor = 0;
            var shadowColors = new[] {
                Color.Argb (0x90, BaseShadowColor, BaseShadowColor, BaseShadowColor).ToArgb (),
                Color.Argb (0, BaseShadowColor, BaseShadowColor, BaseShadowColor).ToArgb ()
            };
            shadowDrawable = new GradientDrawable(GradientDrawable.Orientation.RightLeft,
                                                        shadowColors);
            overlayPaint = new Paint
            {
                Color = Color.Black,
                AntiAlias = true
            };
        }

        private View ContentView
        {
            get
            {
                return FindViewById(Resource.Id.FlyOutContent);
            }
        }

        private View MenuView
        {
            get
            {
                return FindViewById(Resource.Id.FlyOutMenu);
            }
        }

        private int MaxOffset
        {
            get
            {
                return MenuView.Width;
            }
        }

        public bool Opened
        {
            get
            {
                return opened;
            }
            set
            {
                SetOpened(value, animated: false);
            }
        }

        public bool AnimatedOpened
        {
            get
            {
                return opened;
            }
            set
            {
                SetOpened(value, animated: true);
            }
        }

        public void SetOpened(bool opened, bool animated = true)
        {
            if (!animated)
                SetNewOffset(opened ? MaxOffset : 0);
            else
            {
                if (animator != null)
                {
                    animator.Cancel();
                    animator = null;
                }

                animator = ValueAnimator.OfInt(contentOffsetX, opened ? MaxOffset : 0);
                animator.SetInterpolator(interpolator);
                animator.SetDuration(Context.Resources.GetInteger(Android.Resource.Integer.ConfigMediumAnimTime));
                animator.Update += (sender, e) => SetNewOffset((int)e.Animation.AnimatedValue);
                animator.AnimationEnd += (sender, e) => { animator.RemoveAllListeners(); animator = null; };
                animator.Start();
            }
        }

        private void SetNewOffset(int newOffset)
        {
            var oldOffset = contentOffsetX;
            contentOffsetX = Math.Min(Math.Max(0, newOffset), MaxOffset);
            ContentView.OffsetLeftAndRight(contentOffsetX - oldOffset);
            if (opened && contentOffsetX == 0)
                opened = false;
            else if (!opened && contentOffsetX == MaxOffset)
                opened = true;
            UpdateParallax();
            Invalidate();
        }

        private void UpdateParallax()
        {
            var openness = ((float)(MaxOffset - contentOffsetX)) / MaxOffset;
            MenuView.OffsetLeftAndRight((int)(-openness * MaxOffset * ParallaxSpeedRatio) - MenuView.Left);
        }

        public override bool OnInterceptTouchEvent(MotionEvent ev)
        {
            // Only accept single touch
            if (ev.PointerCount != 1)
                return false;

            return CaptureMovementCheck(ev);
        }

        public override bool OnTouchEvent(MotionEvent e)
        {
            if (e.Action == MotionEventActions.Down)
            {
                CaptureMovementCheck(e);
                return true;
            }
            if (!isTracking && !CaptureMovementCheck(e))
                return true;

            if (e.Action != MotionEventActions.Move || MoveDirectionTest(e))
                velocityTracker.AddMovement(e);

            if (e.Action == MotionEventActions.Move)
            {
                var x = e.HistorySize > 0 ? e.GetHistoricalX(0) : e.GetX();
                var traveledDistance = (int)Math.Round(Math.Abs(x - (startX)));
                SetNewOffset(stateBeforeTracking ?
                              MaxOffset - Math.Min(MaxOffset, traveledDistance)
                              : Math.Max(0, traveledDistance));
            }
            else if (e.Action == MotionEventActions.Up
                     && stateBeforeTracking == opened)
            {
                velocityTracker.ComputeCurrentVelocity(1000, maxFlingVelocity);
                if (Math.Abs(velocityTracker.XVelocity) > minFlingVelocity)
                    SetOpened(!opened);
                else if (!opened && contentOffsetX > MaxOffset / 2)
                    SetOpened(true);
                else if (opened && contentOffsetX < MaxOffset / 2)
                    SetOpened(false);
                else
                    SetOpened(opened);

                preTracking = isTracking = false;
            }

            return true;
        }

        private bool CaptureMovementCheck(MotionEvent ev)
        {
            if (ev.Action == MotionEventActions.Down)
            {
                startX = (int)ev.GetX();
                startY = (int)ev.GetY();

                // Only work if the initial touch was in the start strip when the menu is closed
                // When the menu is opened, anywhere will do
                if (!opened && (startX > Context.ToPixels(30)))
                    return false;

                velocityTracker = VelocityTracker.Obtain();
                velocityTracker.AddMovement(ev);
                preTracking = true;
                stateBeforeTracking = opened;
                return false;
            }

            if (ev.Action == MotionEventActions.Up)
                preTracking = isTracking = false;

            if (!preTracking)
                return false;

            velocityTracker.AddMovement(ev);

            if (ev.Action == MotionEventActions.Move)
            {
                // Check we are going in the right direction, if not cancel the current gesture
                if (!MoveDirectionTest(ev))
                {
                    preTracking = false;
                    return false;
                }

                // If the current gesture has not gone long enough don't intercept it just yet
                var distance = Math.Sqrt(Math.Pow(ev.GetX() - startX, 2) + Math.Pow(ev.GetY() - startY, 2));
                if (distance < pagingTouchSlop)
                    return false;
            }

            startX = (int)ev.GetX();
            startY = (int)ev.GetY();
            isTracking = true;

            return true;
        }

        // Check that movement is in a common vertical area and that we are going in the right direction
        private bool MoveDirectionTest(MotionEvent e)
        {
            return (stateBeforeTracking ? e.GetX() <= startX : e.GetX() >= startX)
                && Math.Abs(e.GetY() - startY) < touchSlop;
        }

        protected override void DispatchDraw(Android.Graphics.Canvas canvas)
        {
            base.DispatchDraw(canvas);

            ContentView.OffsetLeftAndRight(contentOffsetX - ContentView.Left);
            UpdateParallax();

            if (opened || isTracking || animator != null)
            {
                // Draw inset shadow on the menu
                canvas.Save();
                shadowDrawable.SetBounds(0, 0, Context.ToPixels(8), Height);
                canvas.Translate(ContentView.Left - shadowDrawable.Bounds.Width(), 0);
                shadowDrawable.Draw(canvas);
                canvas.Restore();

                if (contentOffsetX != 0)
                {
                    // Cover the area with a black overlay to display openess graphically
                    var openness = ((float)(MaxOffset - contentOffsetX)) / MaxOffset;
                    overlayPaint.Alpha = Math.Max(0, (int)(MaxOverlayAlpha * openness));
                    if (overlayPaint.Alpha > 0)
                        canvas.DrawRect(0, 0, ContentView.Left, Height, overlayPaint);
                }
            }
        }

        private class SmoothInterpolator : Java.Lang.Object, ITimeInterpolator
        {
            public float GetInterpolation(float input)
            {
                return (float)Math.Pow(input - 1, 5) + 1;
            }
        }
    }

    internal static class DensityExtensions
    {
        private static readonly DisplayMetrics displayMetrics = new DisplayMetrics();

        public static int ToPixels(this Context ctx, int dp)
        {
            var wm = ctx.GetSystemService(Context.WindowService).JavaCast<IWindowManager>();
            wm.DefaultDisplay.GetMetrics(displayMetrics);

            var density = displayMetrics.Density;
            return (int)(dp * density + 0.5f);
        }
    }
}