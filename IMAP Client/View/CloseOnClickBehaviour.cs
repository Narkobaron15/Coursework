﻿using System.Windows.Controls;
using System.Windows;

namespace IMAP_Client.View
{
    public static class CloseOnClickBehaviour
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(CloseOnClickBehaviour),
                new PropertyMetadata(false, OnIsEnabledPropertyChanged)
            );

        public static bool GetIsEnabled(DependencyObject obj)
        {
            var val = obj.GetValue(IsEnabledProperty);
            return (bool)val;
        }

        public static void SetIsEnabled(DependencyObject obj, bool value)
        {
            obj.SetValue(IsEnabledProperty, value);
        }

        static void OnIsEnabledPropertyChanged(DependencyObject dpo, DependencyPropertyChangedEventArgs args)
        {
            var button = dpo as Button;
            if (button == null)
                return;

            var oldValue = (bool)args.OldValue;
            var newValue = (bool)args.NewValue;

            if (!oldValue && newValue)
            {
                button.Click += OnClick;
            }
            else if (oldValue && !newValue)
            {
                button.PreviewMouseLeftButtonDown -= OnClick;
            }
        }

        static void OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null)
                return;

            var win = Window.GetWindow(button);
            if (win == null)
                return;

            win.Close();
        }

    }
}
