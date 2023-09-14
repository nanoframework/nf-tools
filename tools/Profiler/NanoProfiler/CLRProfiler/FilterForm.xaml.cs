////
// Copyright (c) .NET Foundation and Contributors.
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
////

using CLRProfiler;
using System.Globalization;
using System.Windows;

namespace nanoFramework.Tools.NanoProfiler.CLRProfiler
{
    /// <summary>
    /// Interaction logic for FilterForm.xaml
    /// </summary>
    public partial class FilterForm : Window
    {
        private string[] typeFilters = new string[0];
        internal string[] methodFilters = new string[0];
        internal string[] signatureFilters = new string[0];
        internal ulong[] addressFilters = new ulong[0];
        private bool showChildren = true;
        private bool showParents = true;
        private bool caseInsensitive = true;
        private bool onlyFinalizableTypes = false;

        internal int filterVersion;
        private static int versionCounter;

        internal InterestLevel InterestLevelOfName(string name, string[] typeFilters)
        {
            if (name == "<root>" || typeFilters.Length == 0)
            {
                return InterestLevel.Interesting;
            }

            string bestFilter = "";
            InterestLevel bestLevel = InterestLevel.Ignore;
            foreach (string filter in typeFilters)
            {
                InterestLevel level = InterestLevel.Interesting;
                string realFilter = filter.Trim();
                if (realFilter.Length > 0 && (realFilter[0] == '~' || realFilter[0] == '!'))
                {
                    level = InterestLevel.Ignore;
                    realFilter = realFilter.Substring(1).Trim();
                }
                else
                {
                    if (showParents)
                    {
                        level |= InterestLevel.Parents;
                    }

                    if (showChildren)
                    {
                        level |= InterestLevel.Children;
                    }
                }

                // Check if the filter is a prefix of the name
                if (string.Compare(name, 0, realFilter, 0, realFilter.Length, caseInsensitive, CultureInfo.InvariantCulture) == 0)
                {
                    // This filter matches the type name
                    // Let's see if it's the most specific (i.e. LONGEST) one so far.
                    if (realFilter.Length > bestFilter.Length)
                    {
                        bestFilter = realFilter;
                        bestLevel = level;
                    }
                }
            }
            return bestLevel;
        }

        internal InterestLevel InterestLevelOfAddress(ulong thisAddress)
        {
            if (thisAddress == 0 || addressFilters.Length == 0)
            {
                return InterestLevel.Interesting;
            }

            foreach (ulong address in addressFilters)
            {
                InterestLevel level = InterestLevel.Interesting;
                if (showParents)
                {
                    level |= InterestLevel.Parents;
                }

                if (showChildren)
                {
                    level |= InterestLevel.Children;
                }

                if (address == thisAddress)
                {
                    return level;
                }
            }
            return InterestLevel.Ignore;
        }

        private InterestLevel InterestLevelOfSignature(string signature, string[] signatureFilters)
        {
            if (signature != null && signature != "" && signatureFilters.Length != 0)
            {
                return InterestLevelOfName(signature, signatureFilters);
            }
            else
            {
                return InterestLevel.Interesting | InterestLevel.Parents | InterestLevel.Children;
            }
        }

        internal InterestLevel InterestLevelOfTypeName(string typeName, string signature, bool typeIsFinalizable)
        {
            if (onlyFinalizableTypes && !typeIsFinalizable && typeName != "<root>")
            {
                return InterestLevel.Ignore;
            }
            else
            {
                return InterestLevelOfName(typeName, typeFilters) &
                       InterestLevelOfSignature(signature, signatureFilters);
            }
        }

        internal InterestLevel InterestLevelOfMethodName(string methodName, string signature)
        {
            return InterestLevelOfName(methodName, methodFilters) &
                   InterestLevelOfSignature(signature, signatureFilters);
        }

        internal void SetFilterForm(string typeFilter, string methodFilter, string signatureFilter, string addressFilter,
    bool showAncestors, bool showDescendants, bool caseInsensitive, bool onlyFinalizableTypes)
        {
            typeFilter = typeFilter.Trim();
            if (typeFilter == "")
            {
                typeFilters = new string[0];
            }
            else
            {
                typeFilters = typeFilter.Split(';');
            }

            methodFilter = methodFilter.Trim();
            if (methodFilter == "")
            {
                methodFilters = new string[0];
            }
            else
            {
                methodFilters = methodFilter.Split(';');
            }

            signatureFilter = signatureFilter.Trim();
            if (signatureFilter == "")
            {
                signatureFilters = new string[0];
            }
            else
            {
                signatureFilters = signatureFilter.Split(';');
            }

            addressFilter = addressFilter.Trim();
            if (addressFilter == "")
            {
                addressFilters = new ulong[0];
            }
            else
            {
                string[] addressFilterStrings = addressFilter.Split(';');
                addressFilters = new ulong[addressFilterStrings.Length];
                for (int i = 0; i < addressFilterStrings.Length; i++)
                {
                    string thisAddressFilter = addressFilterStrings[i].Replace(".", "");
                    if (thisAddressFilter != "")
                    {
                        if (thisAddressFilter.StartsWith("0x") || thisAddressFilter.StartsWith("0X"))
                        {
                            addressFilters[i] = ulong.Parse(thisAddressFilter.Substring(2), NumberStyles.HexNumber);
                        }
                        else
                        {
                            addressFilters[i] = ulong.Parse(thisAddressFilter, NumberStyles.HexNumber);
                        }
                    }
                }
            }
            this.showParents = showAncestors;
            this.showChildren = showDescendants;
            this.caseInsensitive = caseInsensitive;
            this.onlyFinalizableTypes = onlyFinalizableTypes;

            this.filterVersion = ++versionCounter;

            typeFilterTextBox.Text = typeFilter;
            methodFilterTextBox.Text = methodFilter;
            signatureFilterTextBox.Text = signatureFilter;
            addressFilterTextBox.Text = addressFilter;

            parentsCheckBox.IsChecked = showParents;
            childrenCheckBox.IsChecked = showChildren;
            caseInsensitiveCheckBox.IsChecked = caseInsensitive;
            onlyFinalizableTypesCheckBox.IsChecked = onlyFinalizableTypes;

        }

        public FilterForm()
        {
            InitializeComponent();
        }

        private void okButton_Click(object sender, RoutedEventArgs e)
        {
            SetFilterForm(
                typeFilterTextBox.Text,
                methodFilterTextBox.Text,
                signatureFilterTextBox.Text,
                addressFilterTextBox.Text,
                parentsCheckBox.IsChecked.Value,
                childrenCheckBox.IsChecked.Value,
                caseInsensitiveCheckBox.IsChecked.Value,
                onlyFinalizableTypesCheckBox.IsChecked.Value);

        }
    }
}
