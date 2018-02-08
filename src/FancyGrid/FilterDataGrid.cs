﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace FancyGrid
{
    /// <inheritdoc />
    /// <summary>
    /// A grid that makes inline filtering possible.
    /// </summary>
    public class FilteringDataGrid : DataGrid
    {
        /// <summary>
        /// This dictionary will have a list of all applied filters
        /// </summary>
        private readonly Dictionary<string, string> columnFilters;

        /// <summary>
        /// This dictionary will map a column to the filter behavior
        /// </summary>
        private readonly Dictionary<string, Func<object, string, bool>> columnFilterModes;

        /// <summary>
        /// Cache with properties for better performance
        /// </summary>
        private readonly Dictionary<string, PropertyInfo> propertyCache;

        /// <summary>
        /// Case sensitive filtering
        /// </summary>
        public static DependencyProperty IsFilteringCaseSensitiveProperty =
            DependencyProperty.Register("IsFilteringCaseSensitive", typeof(bool), typeof(FilteringDataGrid),
                new PropertyMetadata(true));

        /// <summary>
        /// Case sensitive filtering
        /// </summary>
        public bool IsFilteringCaseSensitive
        {
            get => (bool)(GetValue(IsFilteringCaseSensitiveProperty));
            set => SetValue(IsFilteringCaseSensitiveProperty, value);
        }


        /// <summary>
        /// Register for all text changed events
        /// </summary>
        public FilteringDataGrid()
        {
            // Initialize lists
            columnFilters = new Dictionary<string, string>();
            columnFilterModes = new Dictionary<string, Func<object, string, bool>>();
            propertyCache = new Dictionary<string, PropertyInfo>();

            // Enable Filtering
            AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnTextChanged), true);

            // Enable Multisort
            Sorting += FilteringDataGrid_Sorting;

            // Clear the cache if we bind a new collection
            DataContextChanged += FilteringDataGrid_DataContextChanged;

            //Set up context menus
            ContextMenuOpening += FilteringDataGrid_ContextMenuOpening;

            AutoGeneratingColumn += FilteringDataGrid_AutoGeneratingColumn;
        }

        private void FilteringDataGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (!e.Column.CanUserSort)
            {
                var type = e.PropertyType;
                if (type.IsGenericType && type.IsValueType &&
                    typeof(IComparable).IsAssignableFrom(type.GetGenericArguments()[0]))
                {
                    // allow nullable primitives to be sorted
                    e.Column.CanUserSort = true;
                }
            }
        }


        void FilteringDataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender != this)
                return;

            if (ContextMenu == null)
                ContextMenu = new ContextMenu();
            ContextMenu.Items.Clear();
            ContextMenu.Items.Add(new MenuItem
            {
                Header = "Include items like this " + CurrentCell.Item,
                Command = new FunctionRunnerCommand(FilterToSelected)
            });
            ContextMenu.Items.Add(new MenuItem
            {
                Header = "Exclude items like this  " + CurrentCell.Item,
                Command = new FunctionRunnerCommand(FilterToNotSelected)
            });
            ContextMenu.Items.Add(new MenuItem
            {
                Header = "Clear all filters",
                Command = new FunctionRunnerCommand(ClearFilters)
            });
            ContextMenu.Items.Add(new Separator());
            if (ExtraContextMenuItems != null)
                foreach (var item in ExtraContextMenuItems)
                    ContextMenu.Items.Add(ExtraContextMenuItems);
        }

        private void FilterToSelected(object p)
        {
            var tbs = this.AllChildren<TextBox>();
            TextBox tb = null;
            foreach (var item in tbs)
            {
                var header = TryFindParent<DataGridColumnHeader>(item);
                if (header.Column != null && header.Column == CurrentColumn)
                {
                    tb = item;
                    break;
                }
            }

            if (tb != null)
            {
                tb.Text = (CurrentColumn.GetCellContent(CurrentCell.Item) as TextBlock)?.Text ??
                          throw new InvalidOperationException();
            }
        }

        private void FilterToNotSelected(object p)
        {
            var tbs = this.AllChildren<TextBox>();
            TextBox tb = null;
            foreach (var item in tbs)
            {
                var header = TryFindParent<DataGridColumnHeader>(item);
                if (header.Column != null && header.Column == CurrentColumn)
                {
                    tb = item;
                    break;
                }
            }

            var text = (CurrentColumn.GetCellContent(CurrentCell.Item) as TextBlock)?.Text; //Kludge
            if (tb != null)
            {
                tb.Text = "!" + text;
            }
        }

        void ClearFilters(object p)
        {
            foreach (var item in this.AllChildren<TextBox>())
                item.Clear();

            columnFilters.Clear();
            columnFilterModes.Clear();
            ApplyFilters();
        }


        void FilteringDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            var view = CollectionViewSource.GetDefaultView(Items);

            foreach (var column in Columns)
            {
                var sd = Helpers.FindSortDescription(view.SortDescriptions, column.SortMemberPath);
                if (sd.HasValue)
                    column.SortDirection = sd.Value.Direction;
            }

            if (e.Column.SortDirection.HasValue)
            {
                view.SortDescriptions.Remove(
                    view.SortDescriptions.FirstOrDefault(sd => sd.PropertyName == e.Column.Header.ToString()));
                switch (e.Column.SortDirection.Value)
                {
                    case ListSortDirection.Ascending:
                        e.Column.SortDirection = ListSortDirection.Descending;
                        view.SortDescriptions.Add(new SortDescription(e.Column.Header.ToString(),
                            ListSortDirection.Descending));
                        break;
                    case ListSortDirection.Descending:
                        e.Column.SortDirection = null;
                        break;
                    default:
                        break;
                }
            }
            else
            {
                e.Column.SortDirection = ListSortDirection.Ascending;
                view.SortDescriptions.Add(new SortDescription(e.Column.Header.ToString(), ListSortDirection.Ascending));
            }

            e.Handled = true;
            ;
        }

        private void FilteringDataGrid_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            propertyCache.Clear();
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            // Get the textbox
            var filterTextBox = e.OriginalSource as TextBox;

            // Get the header of the textbox
            var header = TryFindParent<DataGridColumnHeader>(filterTextBox);
            if (header != null)
            {
                UpdateFilter(filterTextBox, header);
                ApplyFilters();
            }
        }

        private void UpdateFilter(TextBox textBox, FrameworkElement header)
        {
            // Try to get the property bound to the column.
            // This should be stored as datacontext.
            var columnBinding = header.DataContext?.ToString() ?? "";

            if (!String.IsNullOrEmpty(columnBinding))
            {
                var filter = textBox.Text;

                if (filter.StartsWith("="))
                    columnFilterModes[columnBinding] = fm_is;
                else if (filter.StartsWith("!"))
                    columnFilterModes[columnBinding] = fm_isNot;
                else if (filter.StartsWith("~"))
                    columnFilterModes[columnBinding] = fm_doesNotContain;
                else if (filter.StartsWith("<"))
                    columnFilterModes[columnBinding] = fm_Lessthan;
                else if (filter.StartsWith(">"))
                    columnFilterModes[columnBinding] = fm_GreaterThanEqual;
                else if (filter == "\"\"")
                    columnFilterModes[columnBinding] = fm_blank;
                else if (filter == @"*")
                    columnFilterModes[columnBinding] = fm_notblank;
                else
                    columnFilterModes[columnBinding] = fm_Contains;

                columnFilters[columnBinding] = filter.TrimStart('<', '>', '~', '=', '!');
            }
        }


        private void ApplyFilters()
        {
            // Get the view
            var view = CollectionViewSource.GetDefaultView(ItemsSource);
            view.Filter = Filter;
        }

        private bool Filter(object item)
        {
            // Loop filters
            foreach (var filter in columnFilters)
            {
                var property = GetPropertyValue(item, Regex.Replace(filter.Key, @"\s+", ""));
                if (property != null && !string.IsNullOrEmpty(filter.Value))
                {
                    if (columnFilterModes.ContainsKey(filter.Key))
                    {
                        if (!columnFilterModes[filter.Key](property, filter.Value))
                            return false;
                    }
                    else
                        return fm_Contains(property, filter.Value);
                }
            }

            return true;
        }

        #region FilterMethods

        private bool fm_blank(object item, string filter)
        {
            return string.IsNullOrWhiteSpace(item?.ToString());
        }

        private bool fm_notblank(object item, string filter)
        {
            return !fm_blank(item, filter);
        }

        private bool fm_Contains(object item, string filter)
        {
            if (IsFilteringCaseSensitive)
                return item.ToString().Contains(filter);
            return item.ToString().IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool fm_Startswith(object item, string filter)
        {
            var compareMode = IsFilteringCaseSensitive
                ? StringComparison.CurrentCulture
                : StringComparison.CurrentCultureIgnoreCase;

            return item.ToString().StartsWith(filter, compareMode);
        }

        private bool fm_Lessthan(object item, string filter)
        {
            if (double.TryParse(filter, out var b) && double.TryParse(item.ToString(), out var a))
                return a < b;
            return false;
        }

        private bool fm_GreaterThanEqual(object item, string filter)
        {
            return !fm_Lessthan(item, filter);
        }

        private bool fm_Endswith(object item, string filter)
        {
            var compareMode = IsFilteringCaseSensitive
                ? StringComparison.CurrentCulture
                : StringComparison.CurrentCultureIgnoreCase;
            return item.ToString().EndsWith(filter, compareMode);
        }

        private bool fm_is(object item, string filter)
        {
            var a = item.ToString();
            if (IsFilteringCaseSensitive)
            {
                a = a.ToLower();
                filter = filter.ToLower();
            }

            return a == filter;
        }

        private bool fm_isNot(object item, string filter)
        {
            return !fm_is(item, filter);
        }

        private bool fm_doesNotContain(object item, string filter)
        {
            return !fm_Contains(item, filter);
        }

        #endregion

        /// <summary>
        /// Get the value of a property
        /// </summary>
        /// <param name="item"></param>
        /// <param name="property"></param>
        /// <returns></returns>
        private object GetPropertyValue(object item, string property)
        {
            // No value
            object value = null;

            // Get property  from cache
            PropertyInfo pi;
            if (propertyCache.ContainsKey(property))
                pi = propertyCache[property];
            else
            {
                pi = item.GetType().GetProperty(property,
                    BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
                propertyCache.Add(property, pi);
            }

            // If we have a valid property, get the value
            if (pi != null)
                value = pi.GetValue(item, null);

            // Done
            return value;
        }

        /// <summary>
        /// Finds a parent of a given item on the visual tree.
        /// </summary>
        /// <typeparam name="T">The type of the queried item.</typeparam>
        /// <param name="child">A direct or indirect child of the queried item.</param>
        /// <returns>The first parent item that matches the submitted
        /// type parameter. If not matching item can be found, a null reference is being returned.</returns>
        public static T TryFindParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (true)
            {
                //get parent item
                var parentObject = GetParentObject(child);

                //we've reached the end of the tree
                if (parentObject == null) return null;

                //check if the parent matches the type we're looking for
                if (parentObject is T parent)
                {
                    return parent;
                }

                //use recursion to proceed with next level
                child = parentObject;
            }
        }

        /// <summary>
        /// This method is an alternative to WPF's
        /// <see cref="VisualTreeHelper.GetParent"/> method, which also
        /// supports content elements. Do note, that for content element,
        /// this method falls back to the logical tree of the element.
        /// </summary>
        /// <param name="child">The item to be processed.</param>
        /// <returns>The submitted item's parent, if available. Otherwise null.</returns>
        public static DependencyObject GetParentObject(DependencyObject child)
        {
            if (child == null) return null;

            if (child is ContentElement contentElement)
            {
                var parent = ContentOperations.GetParent(contentElement);
                if (parent != null) return parent;

                return contentElement is FrameworkContentElement fce ? fce.Parent : null;
            }

            // If it's not a ContentElement, rely on VisualTreeHelper
            return VisualTreeHelper.GetParent(child);
        }

        public IEnumerable<MenuItem> ExtraContextMenuItems { get; set; }
    }
}