using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace FModel.Views.Resources.Controls;

/// <summary>
/// Editable ComboBox whose drop-down is filtered by the typed text.
/// https://stackoverflow.com/a/58066259/13389331
/// Its template is the "FilterableComboBox" ComboBox style class in ControlStyles.axaml (needs PART_EditableTextBox).
/// Items not matching the filter are hidden (their containers are collapsed), the ItemsSource is never replaced.
/// </summary>
public class FilterableComboBox : ComboBox
{
    protected override Type StyleKeyOverride => typeof(ComboBox);

    /// <summary>
    /// If true, on lost focus or enter key pressed, checks the text in the combobox. If the text is not present
    /// in the list, it leaves it blank.
    /// </summary>
    public static readonly StyledProperty<bool> OnlyValuesInListProperty =
        AvaloniaProperty.Register<FilterableComboBox, bool>(nameof(OnlyValuesInList));
    public bool OnlyValuesInList
    {
        get => GetValue(OnlyValuesInListProperty);
        set => SetValue(OnlyValuesInListProperty, value);
    }

    /// <summary>
    /// Selected item, changes only on lost focus or enter key pressed
    /// </summary>
    public static readonly StyledProperty<object> EffectivelySelectedItemProperty =
        AvaloniaProperty.Register<FilterableComboBox, object>(nameof(EffectivelySelectedItem));
    public object EffectivelySelectedItem
    {
        get => GetValue(EffectivelySelectedItemProperty);
        set => SetValue(EffectivelySelectedItemProperty, value);
    }

    private string CurrentFilter = string.Empty;
    private bool TextBoxFreezed;
    private int _selectionUpdating;
    private TextBox EditableTextBox;
    private readonly UserChange<bool> IsDropDownOpenUC;
    private readonly DispatcherTimer FilterTimer;

    /// <summary>
    /// Triggers on lost focus or enter key pressed, if the selected item changed since the last time focus was lost or enter was pressed.
    /// </summary>
    public event Action<FilterableComboBox, object> SelectionEffectivelyChanged;

    public FilterableComboBox()
    {
        Classes.Add("FilterableComboBox");
        IsDropDownOpenUC = new UserChange<bool>(v => IsDropDownOpen = v);
        FilterTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(125)
        };
        FilterTimer.Tick += (_, _) =>
        {
            FilterTimer.Stop();
            RefreshFilter();
        };
        DropDownOpened += FilteredComboBox_DropDownOpened;

        Focusable = true;
        IsEditable = true;

        SelectionChanged += (_, __) => shouldTriggerSelectedItemChanged = true;
        SelectionEffectivelyChanged += (_, o) => EffectivelySelectedItem = o;
        ContainerPrepared += (_, e) => e.Container.IsVisible = FilterItem(ItemFromContainer(e.Container));

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        EditableTextBox = e.NameScope.Find<TextBox>("PART_EditableTextBox");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == SelectedItemProperty || change.Property == SelectedIndexProperty)
        {
            // the text set by the selection is not a user change
            _selectionUpdating++;
            try
            {
                base.OnPropertyChanged(change);
            }
            finally
            {
                _selectionUpdating--;
            }
            return;
        }

        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            if (_selectionUpdating == 0 && !TextBoxFreezed && EditableTextBox is { IsKeyboardFocusWithin: true })
                FilteredComboBox_UserTextChange();
        }
        else if (change.Property == ItemsSourceProperty)
        {
            FilterTimer.Stop();
            ApplyFilterToContainers();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsDropDownOpen && e.KeyModifiers == KeyModifiers.None && e.Key is Key.Up or Key.Down)
        {
            MoveSelection(e.Key);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down && !IsDropDownOpen)
        {
            ApplyPendingFilter();
            IsDropDownOpen = true;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ClearFilter();
            Text = "";
            IsDropDownOpen = true;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter || e.Key == Key.Tab)
        {
            ApplyPendingFilter();
            CheckSelectedItem();
            TriggerSelectedItemChanged();
            if (e.Key == Key.Enter && IsDropDownOpen)
            {
                IsDropDownOpen = false;
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Space && IsDropDownOpen)
        {
            // the space character still reaches the text box (TextInput), do not let the ComboBox close the drop-down
            e.Handled = true;
        }
    }

    private void MoveSelection(Key key)
    {
        ApplyPendingFilter();

        var visible = new List<object>();
        foreach (var item in Items)
            if (FilterItem(item))
                visible.Add(item);
        if (visible.Count == 0)
            return;

        var index = SelectedItem == null ? -1 : visible.IndexOf(SelectedItem);
        index = index < 0
            ? key == Key.Up ? visible.Count - 1 : 0
            : Math.Clamp(index + (key == Key.Up ? -1 : 1), 0, visible.Count - 1);

        SelectedItem = visible[index];
        if (ContainerFromItem(SelectedItem) is { } container)
            container.BringIntoView();
        else
            ScrollIntoView(SelectedItem);

        // keep the keyboard in the text box
        EditableTextBox?.Focus();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        ApplyPendingFilter();
        CheckSelectedItem();
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsKeyboardFocusWithin && !IsDropDownOpen)
                TriggerSelectedItemChanged();
        });
    }

    private void CheckSelectedItem()
    {
        if (OnlyValuesInList)
            Text = SelectedItem?.ToString() ?? "";
    }

    private bool shouldTriggerSelectedItemChanged = false;
    private void TriggerSelectedItemChanged()
    {
        if (shouldTriggerSelectedItemChanged)
        {
            SelectionEffectivelyChanged?.Invoke(this, SelectedItem);
            shouldTriggerSelectedItemChanged = false;
        }
    }

    public void ClearFilter()
    {
        FilterTimer.Stop();
        if (string.IsNullOrEmpty(CurrentFilter))
            return;
        CurrentFilter = "";
        ApplyFilterToContainers();
    }

    private void FilteredComboBox_DropDownOpened(object sender, EventArgs e)
    {
        if (IsDropDownOpenUC.IsUserChange)
            ClearFilter();
    }

    private void FilteredComboBox_UserTextChange()
    {
        var tb = EditableTextBox;
        var selStart = Math.Min(tb.SelectionStart, tb.SelectionEnd);
        var selEnd = Math.Max(tb.SelectionStart, tb.SelectionEnd);
        var text = Text ?? string.Empty;
        if (selEnd > selStart && selEnd == text.Length && selStart <= text.Length)
            CurrentFilter = text[..selStart];
        else
            CurrentFilter = text;

        FilterTimer.Stop();
        FilterTimer.Start();
    }

    private void ApplyPendingFilter()
    {
        if (!FilterTimer.IsEnabled)
            return;
        FilterTimer.Stop();
        RefreshFilter();
    }

    private void ApplyFilterToContainers()
    {
        foreach (var container in GetRealizedContainers())
            container.IsVisible = FilterItem(ItemFromContainer(container));
    }

    private void RefreshFilter()
    {
        if (ItemsSource == null)
            return;

        FreezTextBoxState(() =>
        {
            ApplyFilterToContainers();

            if (!IsDropDownOpen && !string.IsNullOrEmpty(CurrentFilter))
                IsDropDownOpenUC.Set(true);

            if (SelectedItem == null)
            {
                foreach (var itm in ItemsSource)
                    if (itm?.ToString() == Text)
                    {
                        SelectedItem = itm;
                        break;
                    }
            }
        });
    }

    private void FreezTextBoxState(Action action)
    {
        TextBoxFreezed = true;
        var tb = EditableTextBox;
        var text = Text;
        var selStart = tb?.SelectionStart ?? 0;
        var selEnd = tb?.SelectionEnd ?? 0;
        var caret = tb?.CaretIndex ?? 0;
        try
        {
            action();
            Text = text;
            if (tb != null)
            {
                tb.CaretIndex = caret;
                tb.SelectionStart = selStart;
                tb.SelectionEnd = selEnd;
            }
        }
        finally
        {
            TextBoxFreezed = false;
        }
    }

    private bool FilterItem(object value)
    {
        if (value == null)
            return false;
        if (CurrentFilter.Length == 0)
            return true;

        var text = value.ToString();
        if (text?.StartsWith("GAME_", StringComparison.OrdinalIgnoreCase) == true)
            text = text[5..];

        return text?.Contains(CurrentFilter, StringComparison.OrdinalIgnoreCase) == true;
    }

    private class UserChange<T>
    {
        private Action<T> action;

        public bool IsUserChange { get; private set; } = true;

        public UserChange(Action<T> action)
        {
            this.action = action;
        }

        public void Set(T val)
        {
            try
            {
                IsUserChange = false;
                action(val);
            }
            finally
            {
                IsUserChange = true;
            }
        }
    }
}
