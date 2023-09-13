////
// Copyright (c) .NET Foundation and Contributors.
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
////

using System.Windows;

namespace nanoFramework.Tools.NanoProfiler.CLRProfiler
{
    /// <summary>
    /// Interaction logic for CommentRangeForm.xaml
    /// </summary>
    public partial class CommentRangeForm : Window
    {
        internal const string startCommentString = "Start of Application";
        internal const string shutdownCommentString = "Shutdown of Application";

        public CommentRangeForm()
        {
            InitializeComponent();
        }
    }
}
