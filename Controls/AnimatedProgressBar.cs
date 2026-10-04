using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace UBFLauncher.Controls;

public sealed class AnimatedProgressBar : ProgressBar
{
    private const string ShimmerPartName = "ProgressShimmer";
    private const string IndeterminatePartName = "IndeterminateIndicator";

    private FrameworkElement? _shimmerElement;
    private FrameworkElement? _indeterminateElement;

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

        _shimmerElement = GetTemplateChild(ShimmerPartName) as FrameworkElement;
        _indeterminateElement = GetTemplateChild(IndeterminatePartName) as FrameworkElement;

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
        if (!IsLoaded || _shimmerElement is null) return;

        _shimmerElement.BeginAnimation(FrameworkElement.MarginProperty,
            CreateMarquee(-100, Math.Max(320, ActualWidth + 100), 1.35));
    }

    private void UpdateIndeterminateAnimation()
    {
        if (_indeterminateElement is null) return;

        if (!IsLoaded || !IsIndeterminate)
        {
            _indeterminateElement.BeginAnimation(FrameworkElement.MarginProperty, null);
            _indeterminateElement.Margin = new Thickness(-110, 0, 0, 0);
            return;
        }

        _indeterminateElement.BeginAnimation(FrameworkElement.MarginProperty,
            CreateMarquee(-110, Math.Max(320, ActualWidth + 100), 0.85));
    }

    private void StopAnimations()
    {
        _shimmerElement?.BeginAnimation(FrameworkElement.MarginProperty, null);
        _indeterminateElement?.BeginAnimation(FrameworkElement.MarginProperty, null);
    }

    private static ThicknessAnimation CreateMarquee(double from, double to, double seconds) => new()
    {
        From = new Thickness(from, 0, 0, 0),
        To = new Thickness(to, 0, 0, 0),
        Duration = TimeSpan.FromSeconds(seconds),
        RepeatBehavior = RepeatBehavior.Forever
    };
}
