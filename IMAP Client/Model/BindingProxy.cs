﻿using System.Windows;

namespace IMAP_Client.Model
{
    public class BindingProxy : Freezable
    {
        protected override Freezable CreateInstanceCore()
            => new BindingProxy();

        public object Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register("Data", typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));
    }
}
