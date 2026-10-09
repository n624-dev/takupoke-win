using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Takupoke.Win;

public sealed partial class MainWindow
{
    private sealed class PopupRegistration { }
    private readonly ConditionalWeakTable<DependencyObject, PopupRegistration> _registeredPopupControls = new();
    private readonly ConditionalWeakTable<FrameworkElement, PopupRegistration> _registeredPopupRoots = new();
    private readonly Dictionary<object, Action> _activePopups = [];
    private bool _popupRenderPending;
    private bool _popupRenderQueued;
    private bool _clearingPrivatePopups;
    private int _offlineDisplayOpened;
    private int _offlineDisplayClosed;

    private void RegisterPopupTree(UIElement root)
    {
        ScanPopupTree(root, new HashSet<DependencyObject>());
        // Add runs before templates enter the visual tree. Scan again after
        // layout; only these roots receive Loaded handlers, not every text row.
        if (root is FrameworkElement element && !_registeredPopupRoots.TryGetValue(element, out _))
        {
            _registeredPopupRoots.Add(element, new());
            element.Loaded += (_, _) => ScanPopupTree(element, new HashSet<DependencyObject>());
        }
    }
    private void ScanPopupTree(DependencyObject element, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(element)) return;
        RegisterPopupControl(element);
        if (element is Panel panel) foreach (var child in panel.Children) ScanPopupTree(child, visited);
        if (element is Border { Child: { } borderChild }) ScanPopupTree(borderChild, visited);
        if (element is ContentControl { Content: DependencyObject content }) ScanPopupTree(content, visited);
        if (element is ContentPresenter { Content: DependencyObject presented }) ScanPopupTree(presented, visited);
        if (element is ItemsControl items) foreach (var item in items.Items.OfType<DependencyObject>()) ScanPopupTree(item, visited);
        if (element is UIElement)
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++) ScanPopupTree(VisualTreeHelper.GetChild(element, index), visited);
    }
    private void RegisterPopupControl(DependencyObject element)
    {
        if (_registeredPopupControls.TryGetValue(element, out _)) return;
        _registeredPopupControls.Add(element, new());
        if (element is FrameworkElement framework)
        {
            if (framework.ContextFlyout is { } context) RegisterPopupControl(context);
            if (FlyoutBase.GetAttachedFlyout(framework) is { } attached) RegisterPopupControl(attached);
        }
        switch (element)
        {
            case DropDownButton { Flyout: { } dropDownFlyout }: RegisterPopupControl(dropDownFlyout); break;
            case SplitButton { Flyout: { } splitFlyout }: RegisterPopupControl(splitFlyout); break;
            case Button { Flyout: { } buttonFlyout }: RegisterPopupControl(buttonFlyout); break;
            case ComboBox combo:
                combo.DropDownOpened += (_, _) => PopupOpened(combo, () => combo.IsDropDownOpen = false);
                combo.DropDownClosed += (_, _) => PopupClosed(combo);
                combo.Unloaded += (_, _) => PopupClosed(combo);
                break;
            case CalendarDatePicker calendar:
                calendar.Opened += (_, _) => PopupOpened(calendar, () => calendar.IsCalendarOpen = false);
                calendar.Closed += (_, _) => PopupClosed(calendar);
                calendar.Unloaded += (_, _) => PopupClosed(calendar);
                break;
            case FlyoutBase flyout:
                flyout.Opening += (_, _) => PopupOpened(flyout, flyout.Hide);
                flyout.Closed += (_, _) => PopupClosed(flyout);
                break;
        }
    }
    private void PopupOpened(object popup, Action close) => _activePopups[popup] = close;
    private void RecordOfflineDisplayLifecycle(bool opened)
    {
        if (!_model.OfflineTest) return;
        if (opened) _offlineDisplayOpened++; else _offlineDisplayClosed++;
        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(_model.Root, "offline-display-lifecycle.txt"),
                $"pid={Environment.ProcessId};opened={_offlineDisplayOpened};closed={_offlineDisplayClosed}");
        }
        catch { }
    }
    private void RecordOfflineDisplayClick(bool completed, bool selected)
    {
        if (!_model.OfflineTest) return;
        // Observe the existing Click/save path only in the fake-data app.
        // The probe contains fixed Boolean fields, never school content.
        try
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(_model.Root, "offline-display-clicks.txt"),
                $"completed={(completed ? 1 : 0)};selected={(selected ? 1 : 0)};"
                + $"current={(_model.Preferences.IncludesChanges ? 1 : 0)}\n");
        }
        catch { }
    }
    private void PopupClosed(object popup)
    {
        _activePopups.Remove(popup);
        QueuePopupRender();
    }
    private bool DeferRenderForPopups()
    {
        if (_activePopups.Count == 0) return false;
        _popupRenderPending = true;
        return true;
    }
    private void QueuePopupRender()
    {
        if (_clearingPrivatePopups || !_popupRenderPending || _activePopups.Count > 0 || _popupRenderQueued) return;
        _popupRenderQueued = true;
        // Closed can arrive before the menu item's Click/selection callback.
        // Flush at low priority so the selected action starts before rebuilding.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _popupRenderQueued = false;
            if (!_popupRenderPending || _activePopups.Count > 0) return;
            _popupRenderPending = false;
            Render();
        });
    }
    private void ClosePrivatePopups()
    {
        _clearingPrivatePopups = true;
        try
        {
            foreach (var close in _activePopups.Values.ToArray()) close();
            _activePopups.Clear(); _popupRenderPending = false;
            // Privacy clearing cannot wait for a popup/dialog close animation.
            PageContent.Children.Clear(); _renderedPage = null;
        }
        finally { _clearingPrivatePopups = false; }
    }
}
