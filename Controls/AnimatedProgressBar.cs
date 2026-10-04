using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace UBFLauncher.Controls;

public sealed class AnimatedProgressBar : ProgressBar
{
    private const string ShimmerPartName = "ProgressShimmer";
    private const string IndeterminatePartName = "IndeterminateIndicator";

    private TranslateTransform? _shimmerTransform;
    private TranslateTransform? _indeterminateTransform;

    public AnimatedProgressBar()
    {
        Loaded += (_, _) => StartAnimations();
        Unloaded += (_, _) => StopAnimations();
        SizeChanged += (_, _) => RestartShimmerAnimation();
    }

    public override void OnApplyTemplate()
    {
        StopAnimations();
        base.OnApplyTemplate();

        _shimmerTransform = GetTransform(GetTemplateChild(ShimmerPartName) as FrameworkElement);
        _indeterminateTransform = GetTransform(GetTemplateChild(IndeterminatePartName) as FrameworkElement);

        StartAnimations();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == IsIndeterminateProperty)
        {
            UpdateIndeterminateAnimation();
        }
    }

    private void StartAnimations()
    {
        if (!IsLoaded) return;

        RestartShimmerAnimation();
        UpdateIndeterminateAnimation();
    }

    private void RestartShimmerAnimation()
    {
        if (!IsLoaded || _shimmerTransform is null) return;

        _shimmerTransform.BeginAnimation(TranslateTransform.XProperty, CreateMarquee(-100, Math.Max(320, ActualWidth + 100), 1.35));
    }

    private void UpdateIndeterminateAnimation()
    {
        if (_indeterminateTransform is null) return;

        if (!IsLoaded || !IsIndeterminate)
        {
            _indeterminateTransform.BeginAnimation(TranslateTransform.XProperty, null);
            _indeterminateTransform.X = -110;
            return;
        }

        _indeterminateTransform.BeginAnimation(TranslateTransform.XProperty, CreateMarquee(-110, Math.Max(320, ActualWidth + 100), 0.85));
    }

    private void StopAnimations()
    {
        _shimmerTransform?.BeginAnimation(TranslateTransform.XProperty, null);
        _indeterminateTransform?.BeginAnimation(TranslateTransform.XProperty, null);
    }

    private static TranslateTransform? GetTransform(FrameworkElement? element)
    {
        if (element is null) return null;
        var transform = new TranslateTransform();
        element.RenderTransform = transform;
        return transform;
    }

    private static DoubleAnimation CreateMarquee(double from, double to, double seconds) => new()
    {
        From = from,
        To = to,
        Duration = TimeSpan.FromSeconds(seconds),
        RepeatBehavior = RepeatBehavior.Forever
    };
}
