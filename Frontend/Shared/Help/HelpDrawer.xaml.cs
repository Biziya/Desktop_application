using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Frontend.Shared.Infrastructure;

namespace Frontend.Shared.Help;

/// <summary>
/// The Help drawer's view: slides in and out (200 ms / 150 ms, instant when Windows animations are off), keeps its
/// width within the window (and remembers a dragged width), restores each page's scroll position for the session,
/// scrolls to a section for the chips and search results, highlights the chip of the section in view, and moves
/// focus in on open and back on close. Focus is never trapped: Tab can leave the drawer.
/// </summary>
public partial class HelpDrawer : UserControl
{
    private const double SectionTolerance = 24;
    private static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan CloseDuration = TimeSpan.FromMilliseconds(150);

    private HelpPanel? panel;
    private HelpEntry? shownEntry;
    private IInputElement? focusBeforeOpen;
    private DateTime ignoreScrollUntil;
    private double? dragWidth;
    private bool caretFollowsSlide;

    public HelpDrawer()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Attach(e.NewValue as HelpPanel);
        SizeChanged += (_, _) => FitWidth();
    }

    /// <summary>Windows' "Animation effects" setting; tests replace it.</summary>
    public static Func<bool> AnimationsEnabled { get; set; } = () => SystemParameters.ClientAreaAnimation;

    /// <summary>The drawer itself (the sliding panel), for tests and screenshots.</summary>
    public FrameworkElement Panel => Drawer;

    /// <summary>Horizontal slide of the drawer: 0 when fully open.</summary>
    public double SlideOffset => Slide.X;

    public TextBox SearchField => SearchBox;

    public ScrollViewer PageScroller => Scroller;

    public Thumb WidthGrip => ResizeGrip;

    /// <summary>True when <paramref name="element"/> is in the drawer or the lightbox (not the backdrop).</summary>
    public bool Contains(object? element)
    {
        for (var node = element as DependencyObject; node is not null; node = Parent(node))
        {
            if (ReferenceEquals(node, Drawer) || ReferenceEquals(node, Lightbox))
            {
                return true;
            }
        }

        return false;

        static DependencyObject? Parent(DependencyObject node) =>
            node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
    }

    // The element with the focus in this window: the keyboard focus, or the window's logical focus while it is inactive.
    private IInputElement? FocusedElement()
    {
        var window = Window.GetWindow(this);

        if (Keyboard.FocusedElement is DependencyObject keyboard and not Window && ReferenceEquals(Window.GetWindow(keyboard), window))
        {
            return (IInputElement)keyboard;
        }

        return window is null ? null : FocusManager.GetFocusedElement(window) is { } logical and not Window ? logical : null;
    }

    /// <summary>Ctrl+F: focuses the search box and selects its text.</summary>
    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void Attach(HelpPanel? help)
    {
        if (panel is not null)
        {
            panel.PropertyChanged -= PanelChanged;
            panel.SectionRequested -= ScrollToSection;
        }

        panel = help;

        if (panel is not null)
        {
            panel.PropertyChanged += PanelChanged;
            panel.SectionRequested += ScrollToSection;
            shownEntry = panel.Selected;
            UpdateBackdrop();
            UpdateLightbox();

            if (panel.IsOpen)
            {
                Open();
            }
        }
    }

    private void PanelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HelpPanel.IsOpen) when panel!.IsOpen:
                Open();
                break;
            case nameof(HelpPanel.IsOpen):
                Close();
                break;
            case nameof(HelpPanel.ShowsBackdrop):
                UpdateBackdrop();
                break;
            case nameof(HelpPanel.IsLightboxOpen):
                UpdateLightbox();
                break;
            case nameof(HelpPanel.Selected):
                ShowEntry(panel!.Selected);
                break;
        }
    }

    private void UpdateBackdrop() => Backdrop.Visibility = panel?.ShowsBackdrop == true ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateLightbox()
    {
        var open = panel?.IsLightboxOpen == true;
        Lightbox.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

        if (open)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => LightboxClose.Focus());
        }
        else if (Drawer.IsVisible && (FocusedElement() is not { } inside || Contains(inside) || LightboxClose.IsKeyboardFocused))
        {
            Screenshot.Focus();
        }
    }

    // ===== Open / close =====

    private void Open()
    {
        var wasVisible = Drawer.Visibility == Visibility.Visible;

        if (!wasVisible)
        {
            focusBeforeOpen = FocusedElement() is { } focused && !Contains(focused) ? focused : null;
        }

        FitWidth();
        Drawer.Visibility = Visibility.Visible;
        // Reopened while still sliding out: continue from where it is.
        var from = wasVisible ? Math.Min(Slide.X, Drawer.Width) : Drawer.Width;
        // The search box gets focus while the drawer is still sliding in. WPF places the text caret (an adorner) only
        // on layout, which the slide transform does not cause, so the caret would stay where the box was when focus
        // landed: partway into the box. Re-place it on every step of the slide, and once more when it ends.
        FollowSlideWithCaret(true);
        Animate(from, 0, OpenDuration, EasingMode.EaseOut, () =>
        {
            FollowSlideWithCaret(false);
            UpdateCaret();
        });
        RestoreScroll();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (panel?.IsOpen == true)
            {
                SearchBox.Focus();
            }
        });
    }

    // Freezable.Changed throws when removing a handler that is not attached, so track it.
    private void FollowSlideWithCaret(bool follow)
    {
        if (follow == caretFollowsSlide)
        {
            return;
        }

        caretFollowsSlide = follow;

        if (follow)
        {
            Slide.Changed += SlideMoved;
        }
        else
        {
            Slide.Changed -= SlideMoved;
        }
    }

    private void SlideMoved(object? sender, EventArgs e) => UpdateCaret();

    /// <summary>Moves the search box's text caret to where the box is now drawn.</summary>
    private void UpdateCaret()
    {
        if (SearchBox.IsKeyboardFocused)
        {
            AdornerLayer.GetAdornerLayer(SearchBox)?.Update();
        }
    }

    private void Close()
    {
        FollowSlideWithCaret(false);
        RememberScroll();
        var returnTo = focusBeforeOpen;
        focusBeforeOpen = null;
        var focusedNow = FocusedElement();
        var hadFocus = focusedNow is not null && Contains(focusedNow);

        Animate(Slide.X, Drawer.Width, CloseDuration, EasingMode.EaseIn, () =>
        {
            if (panel?.IsOpen != true)
            {
                Drawer.Visibility = Visibility.Collapsed;
            }
        });

        // Back to where the user was (only if the focus was in the drawer; a click elsewhere already moved it).
        if (hadFocus || focusedNow is null)
        {
            if (returnTo is UIElement { IsVisible: true, IsEnabled: true, Focusable: true } element)
            {
                element.Focus();
            }
            else
            {
                Keyboard.ClearFocus();
                Window.GetWindow(this)?.Focus();
            }
        }
    }

    private void Animate(double from, double to, TimeSpan duration, EasingMode easing, Action? completed)
    {
        Slide.BeginAnimation(TranslateTransform.XProperty, null);

        if (!AnimationsEnabled() || !IsLoaded)
        {
            Slide.X = to;
            completed?.Invoke();
            return;
        }

        Slide.X = from;
        var animation = new DoubleAnimation(from, to, duration)
        {
            EasingFunction = new CubicEase { EasingMode = easing },
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            Slide.X = to;
            completed?.Invoke();
        };
        Slide.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    // ===== Width =====

    private void FitWidth()
    {
        if (panel is null || ActualWidth <= 0)
        {
            return;
        }

        Drawer.Width = HelpPanel.WidthFor(dragWidth ?? panel.PreferredWidth, ActualWidth);

        if (panel.IsOpen is false && Drawer.Visibility == Visibility.Visible && Slide.HasAnimatedProperties is false)
        {
            Slide.X = Drawer.Width;
        }
    }

    private void ResizeDragged(object sender, DragDeltaEventArgs e)
    {
        dragWidth = HelpPanel.WidthFor((dragWidth ?? Drawer.Width) - e.HorizontalChange, ActualWidth);
        FitWidth();
    }

    private void ResizeFinished(object sender, DragCompletedEventArgs e)
    {
        if (dragWidth is { } width && panel is not null)
        {
            panel.RememberWidth(width);
        }

        dragWidth = null;
        FitWidth();
    }

    // ===== Scrolling =====

    // Before the entry changes: its scroll position (the content still shows it), then the new entry's, after layout.
    private void ShowEntry(HelpEntry? entry)
    {
        RememberScroll();
        shownEntry = entry;
        RestoreScroll();
    }

    private void RememberScroll()
    {
        if (panel is not null && shownEntry is not null && Drawer.Visibility == Visibility.Visible && !panel.IsSearching)
        {
            panel.RememberScrollOffset(shownEntry, Scroller.VerticalOffset);
        }
    }

    private void RestoreScroll()
    {
        if (panel is null || shownEntry is null)
        {
            return;
        }

        var offset = panel.ScrollOffsetOf(shownEntry);
        ignoreScrollUntil = DateTime.UtcNow.AddMilliseconds(100);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            Scroller.UpdateLayout();
            ScrollAnimation.ScrollTo(Scroller, offset, animate: false);
            UpdateActiveSection();
        });
    }

    private FrameworkElement? SectionElement(string section) => section switch
    {
        HelpSections.BeforeYouStart => BeforeSection,
        HelpSections.Steps => StepsSection,
        HelpSections.Items => ItemsSection,
        HelpSections.Troubleshooting => TroubleshootingSection,
        _ => null
    };

    /// <summary>Scroll offset that brings a section's top to the top of the drawer.</summary>
    public double OffsetOf(string section) =>
        SectionElement(section) is { IsVisible: true } element
            ? Math.Max(0, element.TranslatePoint(default, Scroller).Y + Scroller.VerticalOffset - 12)
            : 0;

    private void ScrollToSection(string section)
    {
        ignoreScrollUntil = DateTime.UtcNow.AddMilliseconds(400);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            Scroller.UpdateLayout();
            ScrollAnimation.ScrollTo(Scroller, OffsetOf(section), AnimationsEnabled() && IsLoaded);
        });
    }

    private void ScrollerScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (DateTime.UtcNow >= ignoreScrollUntil && e.VerticalChange != 0)
        {
            UpdateActiveSection();
        }
    }

    // The chip of the last section whose top has scrolled to the top of the drawer; at the very bottom, the last one.
    private void UpdateActiveSection()
    {
        if (panel?.Page is null || !Scroller.IsVisible)
        {
            return;
        }

        var sections = panel.Chips.Select(c => c.Section).ToList();

        if (sections.Count == 0)
        {
            return;
        }

        var active = sections[0];
        var atBottom = Scroller.ScrollableHeight > 0 && Scroller.VerticalOffset >= Scroller.ScrollableHeight - 1;

        foreach (var section in sections)
        {
            if (SectionElement(section) is { IsVisible: true } element && element.TranslatePoint(default, Scroller).Y <= SectionTolerance)
            {
                active = section;
            }
        }

        panel.ActiveSection = atBottom ? sections[^1] : active;
    }

    // ===== Mouse =====

    private void BackdropPressed(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        panel?.Close();
    }

    private void LightboxPressed(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        panel?.CloseLightboxCommand.Execute(null);
    }
}
