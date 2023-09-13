////
// Copyright (c) .NET Foundation and Contributors.
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
////

using System.Windows;
using System.Windows.Controls;

namespace nanoFramework.Tools.NanoProfiler.CLRProfiler
{
    /// <summary>
    /// Interaction logic for ProgressForm.xaml
    /// </summary>
    public partial class ProgressForm : Window
    {
        public ProgressForm()
        {
            InitializeComponent();
        }
        public void SetProgress(int value)
        {
            ProgressBar.Value = value;
        }

        public void SetMaximum(int value)
        {
            ProgressBar.Maximum = value;
        }
    }
}
