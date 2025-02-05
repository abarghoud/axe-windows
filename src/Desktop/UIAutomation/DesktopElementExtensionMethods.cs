﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Axe.Windows.Core.Bases;
using Axe.Windows.Core.Misc;
using Axe.Windows.Core.Types;
using Axe.Windows.Telemetry;
using Axe.Windows.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using UIAutomationClient;

namespace Axe.Windows.Desktop.UIAutomation
{
    /// <summary>
    /// ExtensionMethods for DesktopElement
    /// </summary>
    public static class DesktopElementExtensionMethods
    {
        static readonly List<int> MiniumProperties = new List<int>()
        {
            PropertyType.UIA_NamePropertyId,
            PropertyType.UIA_LocalizedControlTypePropertyId,
            PropertyType.UIA_ControlTypePropertyId,
            PropertyType.UIA_BoundingRectanglePropertyId,
            PropertyType.UIA_RuntimeIdPropertyId,
            PropertyType.UIA_ProcessIdPropertyId
        };

        /// <summary>
        /// Populate member data and testresults are cleaned up.
        /// if there is existing data, clean up first and populate
        /// please don't call it directly but use UpdateElementAction.
        /// it would make Ux and Runtime separation easier since all communication is done via Actions.
        /// </summary>
        /// <param name="element"></param>
        public static void PopulateAllPropertiesWithLiveData(this A11yElement element)
        {
            element.PopulateAllPropertiesWithLiveData(DesktopDataContext.DefaultContext);
        }

        internal static void PopulateAllPropertiesWithLiveData(this A11yElement element, DesktopDataContext dataContext)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            element.Clear();

            if (element.IsSafeToRefresh())
            {
                element.PopulatePropertiesAndPatternsFromCache(dataContext);
                element.PopulatePlatformProperties();
            }
        }

        /// <summary>
        /// Populate element with Minimum Properties for Live Tree
        /// - Name
        /// - LocalizedControlType
        /// - BoundingRectangle
        /// </summary>
        /// <param name="element"></param>
        internal static void PopulateMinimumPropertiesForSelection(this A11yElement element, DesktopDataContext dataContext)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            element.PopulateWithIndicatedProperties(MiniumProperties, dataContext);
        }

        /// <summary>
        /// Update Glimpse
        /// </summary>
        /// <param name="element"></param>
        public static void UpdateGlimpse(this A11yElement element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            element.Glimpse = $"{element.LocalizedControlType} '{Regex.Replace(element.Name ?? "", @"\t|\n|\r", " ")}'";
        }

        private static IEnumerable<A11yProperty> RetrievePropertiesFromCache(IUIAutomationElement e, List<int> list)
        {
            if (e == null) yield break;
            if (list == null) yield break;

            foreach (var p in list)
            {
                var value = GetPropertyValueFromCache(e, p);
                if (value == null) continue;

                yield return new A11yProperty(p, value);
            }
        }

        /// <summary>
        /// Populate element with list of properties
        /// </summary>
        /// <param name="element"></param>
        /// <param name="list"></param>
        private static void PopulateWithIndicatedProperties(this A11yElement element, List<int> list, DesktopDataContext dataContext)
        {
            element.Clear();
            if (element.IsSafeToRefresh())
            {
                dataContext.A11yAutomation.UIAutomation.PollForPotentialSupportedProperties((IUIAutomationElement)element.PlatformObject, out int[] ppids, out string[] ppns);

                // build a cache based on the lists
                var cache = DesktopElementHelper.BuildCacheRequest(list, null, dataContext);

                // buildupdate cached element
                var uia = ((IUIAutomationElement)element.PlatformObject).BuildUpdatedCache(cache);

                var properties = RetrievePropertiesFromCache(uia, list);

                element.Properties = properties.Where(p => !string.IsNullOrEmpty(p.TextValue)).ToDictionary(d => d.Id);

                element.UpdateGlimpse();

                // release previous UIAElement
                Marshal.ReleaseComObject(uia);
                // release cache interface.
                Marshal.ReleaseComObject(cache);
            }
        }

        /// <summary>
        /// Clone A11yElement for selection
        /// </summary>
        /// <param name="e"></param>
        /// <returns>if element is not live, don't allow clone</returns>
        public static A11yElement CloneForSelection(this A11yElement e, DesktopDataContext dataContext)
        {
            if (dataContext == null) throw new ArgumentNullException(nameof(dataContext));

            if (e == null) return null;
            if (e.PlatformObject == null) return null;

            try
            {
                var cache = DesktopElementHelper.BuildCacheRequest(MiniumProperties, null, dataContext);

                var uia = ((IUIAutomationElement)e.PlatformObject).BuildUpdatedCache(cache);
                Marshal.ReleaseComObject(cache);

                var ne = new DesktopElement(uia, true, false);
                ne.PopulateMinimumPropertiesForSelection(dataContext);

                return ne;
            }
            catch (COMException ex)
            {
                ex.ReportException();
            }

            return null;
        }

        /// <summary>
        /// Clear element members.
        /// </summary>
        /// <param name="element"></param>
        public static void Clear(this A11yElement element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            if (element.Properties != null)
            {
                foreach (var pp in element.Properties.Values)
                {
                    pp.Dispose();
                }
                element.Properties.Clear();
                element.Properties = null;
            }

            ListHelper.DisposeAllItemsAndClearList(element.Patterns);
            element.Patterns = null;

            element.PlatformProperties?.Clear();
            element.PlatformProperties = null;

            // since data is changed, test results should be removed.
            element.ScanResults?.Items.Clear();
        }

        /// <summary>
        /// Refresh PlatformPropertiesy
        /// </summary>
        private static void PopulatePlatformProperties(this A11yElement element)
        {
            try
            {
                var pp = element.Properties?.ById(PropertyType.UIA_NativeWindowHandlePropertyId);
                if (pp != null)
                {
                    var handle = new IntPtr(pp.Value);
                    element.PlatformProperties = new Dictionary<int, A11yProperty>();

                    element.AddWindowsExtendedStyleIntoPlatformProperties(handle);
                    element.AddWindowsStyleIntoPlatformProperties(handle);
                }
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception e)
            {
                e.ReportException();
            }
#pragma warning restore CA1031 // Do not catch general exception types
        }

        /// <summary>
        /// Get windows extended style and add it into Platform Properties
        /// </summary>
        private static void AddWindowsExtendedStyleIntoPlatformProperties(this A11yElement element, IntPtr handle)
        {
            var val = Win32.NativeMethods.GetWindowLong(handle, Win32.Win32Constants.GWL_EXSTYLE);
            if (val != 0)
            {
                element.PlatformProperties.Add(PlatformPropertyType.Platform_WindowsExtendedStylePropertyId, new A11yProperty(PlatformPropertyType.Platform_WindowsExtendedStylePropertyId, val));
            }
        }

        /// <summary>
        /// Get windows style and add it into Platform Properties
        /// </summary>
        private static void AddWindowsStyleIntoPlatformProperties(this A11yElement element, IntPtr handle)
        {
            var val = Win32.NativeMethods.GetWindowLong(handle, Win32.Win32Constants.GWL_STYLE);
            if (val != 0)
            {
                element.PlatformProperties.Add(PlatformPropertyType.Platform_WindowsStylePropertyId, new A11yProperty(PlatformPropertyType.Platform_WindowsStylePropertyId, val));
            }
        }

        /// <summary>
        /// Refresh the property and patterns with Live data.
        /// it also set the Glimpse
        /// the update is done via caching to improve performance.
        /// </summary>
        /// <param name="useProperties">default is false to refresh it from UIElement directly</param>
        private static void PopulatePropertiesAndPatternsFromCache(this A11yElement element, DesktopDataContext dataContext)
        {
            try
            {
                // Get the list of properties to retrieve
                dataContext.A11yAutomation.UIAutomation.PollForPotentialSupportedProperties((IUIAutomationElement)element.PlatformObject, out int[] ppids, out string[] ppns);

                var ppl = new List<Tuple<int, string>>();

                for (int i = 0; i < ppids.Length; i++)
                {
                    var id = (int)ppids.GetValue(i);
                    var name = (string)ppns.GetValue(i);

                    if (DesktopElement.IsExcludedProperty(id, name) == false)
                    {
                        ppl.Add(new Tuple<int, string>(id, name));
                    }
                }

                dataContext.A11yAutomation.UIAutomation.PollForPotentialSupportedPatterns((IUIAutomationElement)element.PlatformObject, out int[] ptids, out string[] ptns);
                var ptl = new List<Tuple<int, string>>();

                for (int i = 0; i < ptids.Length; i++)
                {
                    ptl.Add(new Tuple<int, string>((int)ptids.GetValue(i), (string)ptns.GetValue(i)));
                }

                var pplist = (from pp in ppl
                              select pp.Item1).ToList();
                var ptlist = (from pt in ptl
                              select pt.Item1).ToList();

                // build a cache based on the lists
                var cache = DesktopElementHelper.BuildCacheRequest(pplist, ptlist, dataContext);

                // buildupdate cached element
                var uia = ((IUIAutomationElement)element.PlatformObject).BuildUpdatedCache(cache);

                // retrieve properties from cache
                var ps = (from pp in ppl//.AsParallel()
                          let val = GetPropertyValueFromCache(uia, pp.Item1)
                          where val != null
                          select new A11yProperty(pp.Item1, val, pp.Item2));

                element.Properties = ps.Where(p => !string.IsNullOrEmpty(p.TextValue)).ToDictionary(d => d.Id);

                element.UpdateGlimpse();

                element.InitClickablePointProperty(uia, dataContext);

                // retrieve patterns from cache
                var ptlst = from pt in ptl
                            let pi = A11yPatternFactory.GetPatternInstance(element, uia, pt.Item1, pt.Item2)
                            where pi != null
                            select pi;

                element.Patterns = ptlst.ToList();

                // release previous UIAElement
                Marshal.ReleaseComObject(uia);
                // release cache interface.
                Marshal.ReleaseComObject(cache);

                ppl.Clear();
                ptl.Clear();
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception e)
            {
                e.ReportException();
            }
#pragma warning restore CA1031 // Do not catch general exception types
        }

        /// <summary>
        /// Check whether element is safe to refresh
        /// </summary>
        /// <param name="element"></param>
        /// <returns></returns>
        public static bool IsSafeToRefresh(this A11yElement element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            bool ret = false;

            if (element.PlatformObject != null)
            {
                try
                {
                    var pid = ((IUIAutomationElement)element.PlatformObject).GetRuntimeId();
                    ret = true;
                    pid = null;
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch (Exception e)
                {
                    // at this point, UIAElement is invalid. but don't remove.
                    // element.PlatformObject = null;
                    e.ReportException();
                }
#pragma warning restore CA1031 // Do not catch general exception types
            }

            return ret;
        }

        /// <summary>
        /// Get Property value safely
        /// </summary>
        /// <param name="element"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        private static dynamic GetPropertyValue(IUIAutomationElement element, int id)
        {
            dynamic value;
            try
            {
                dynamic temp = ShouldGetPropertyValueNoDefault(id)
                    ? element.GetCurrentPropertyValueEx(id, 1 /*true*/)
                    : element.GetCurrentPropertyValue(id);

                value = ConvertVariantAsNeeded(temp);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception e)
            {
                e.ReportException();
                value = null;
            }
#pragma warning restore CA1031 // Do not catch general exception types

            return value;
        }

        /// <summary>
        /// Get Property value safely
        /// </summary>
        /// <param name="element"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        private static dynamic GetPropertyValueFromCache(IUIAutomationElement element, int id)
        {
            dynamic value;
            try
            {
                dynamic temp = ShouldGetPropertyValueNoDefault(id)
                    ? element.GetCachedPropertyValueEx(id, 1 /*true*/)
                    : element.GetCachedPropertyValue(id);

                value = ConvertVariantAsNeeded(temp);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception e)
            {
                e.ReportException();
                value = null;
            }
#pragma warning restore CA1031 // Do not catch general exception types

            return value;
        }

        /// <summary>
        /// When it cannot get a value, IUIAutomationElement.GetCachedPropertyValue
        /// returns the default value for the expected type.
        /// In some cases, the default value is indistinguishable from a valid return value.
        /// Therefore, we want to know if the value is intentional or unintentional.
        /// </summary>
        /// <param name="propertyId"></param>
        /// <returns></returns>
        private static bool ShouldGetPropertyValueNoDefault(int propertyId)
        {
            switch (propertyId)
            {
                case PropertyType.UIA_BoundingRectanglePropertyId:
                    return true;
            } // switch

            return false;
        }

        /// <summary>
        /// Convert Variant Value to string if they are IUIAxxxxx interfaces
        /// </summary>
        /// <param name="variant"></param>
        /// <returns></returns>
        private static dynamic ConvertVariantAsNeeded(dynamic variant)
        {
            if (variant != null)
            {
                if (variant is IUIAutomationElement)
                {
                    var tmp = variant;
                    variant = GetGlimpseOfUIAElement(variant);
#pragma warning disable CA1806 // Do not ignore method results
                    NativeMethods.VariantClear(ref tmp);
                }
                else if (variant is IUIAutomationElementArray)
                {
                    var tmp = variant;
                    variant = GetGlimpsesOfUIAElements(variant);
                    NativeMethods.VariantClear(ref tmp);
                }
                else if (Marshal.IsComObject(variant))
                {
                    /* When checked against the four common return types for
                     * IUIAutomation.GetPropertyValueEx: int, bool, string, and double[]
                     * IsComObject always returned false.
                     * And when the type tested was System.__ComObject,
                     * the return value was always true
                     * and the intent of the return value was to indicate invalidity.
                     * Also, I have not seen IsComObject return true when the type was not __ComObject.
                     * Therefore, we are treating __ComObject as a failure and returning null.
                     */
                    NativeMethods.VariantClear(ref variant);
#pragma warning restore CA1806 // Do not ignore method results
                    return null;
                }
            }

            return variant;
        }

        /// <summary>
        /// Get Glimpse of UIA Element
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        private static string GetGlimpseOfUIAElement(IUIAutomationElement e)
        {
            StringBuilder sb = new StringBuilder();
            var name = GetPropertyValue(e, PropertyType.UIA_NamePropertyId);
            var lct = GetPropertyValue(e, PropertyType.UIA_LocalizedControlTypePropertyId);
            sb.AppendFormat("{1} \"{0}\"", name, lct);
#pragma warning disable CA1806 // Do not ignore method results
            NativeMethods.VariantClear(ref name);
            NativeMethods.VariantClear(ref lct);
#pragma warning restore CA1806 // Do not ignore method results
            return sb.ToString();
        }

        /// <summary>
        /// Get Glimpses of UIA Elements
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        private static string GetGlimpsesOfUIAElements(IUIAutomationElementArray arr)
        {
            if (arr.Length != 0)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append('[');

                for (int i = 0; i < arr.Length; i++)
                {
                    var e = arr.GetElement(i);

                    if (i != 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append($"{GetGlimpseOfUIAElement(e)}");
                    Marshal.ReleaseComObject(e);
                }

                sb.Append(']');
                return sb.ToString();
            }

            return null;
        }

        /// <summary>
        /// Adds the value of the ClickablePoint property to the A11yElement's Properties dictionary
        /// </summary>
        /// <remarks>
        /// Requesting the clickable point property in Edge can cause a crash,
        /// so the clickable point property is not initially populated by <see cref="DesktopElementExtensionMethods.PopulatePropertiesAndPatternsFromCache(A11yElement)"/>.
        /// </remarks>
        private static void InitClickablePointProperty(this A11yElement a11yElement, IUIAutomationElement uiaElement, DesktopDataContext dataContext)
        {
            if (a11yElement.IsEdgeElement()) return;

            int id = PropertyType.UIA_ClickablePointPropertyId;

            double[] clickablePoint = uiaElement.GetCurrentPropertyValue(id);
            if (clickablePoint == null) return;

            string name = dataContext.A11yAutomation.UIAutomation.GetPropertyProgrammaticName(id);

            var prop = new A11yProperty
            {
                Id = id,
                Name = name,
                Value = new Point((int)clickablePoint[0], (int)clickablePoint[1])
            };

            a11yElement.Properties.Add(id, prop);
        }

        private static bool IsEdgeElement(this IA11yElement e)
        {
            return e.Framework == Axe.Windows.Core.Enums.FrameworkId.Edge;
        }
    }
}
