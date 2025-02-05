﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Axe.Windows.Desktop.Types;
using System.Collections.Generic;
using UIAutomationClient;

namespace Axe.Windows.Desktop.UIAutomation.EventHandlers
{
    /// <summary>
    /// ActiveTextPositionChangedEvent listener.
    /// this event is available from Win10 RS5
    /// </summary>
    public class ActiveTextPositionChangedEventListener : EventListenerBase, IUIAutomationActiveTextPositionChangedEventHandler
    {
        /// <summary>
        /// Create an event handler and register it.
        /// </summary>
        public ActiveTextPositionChangedEventListener(CUIAutomation8 uia8, IUIAutomationElement element, TreeScope scope, HandleUIAutomationEventMessage peDelegate) : base(uia8, element, scope, EventType.UIA_NotificationEventId, peDelegate)
        {
            Init();
        }

        public override void Init()
        {
            IUIAutomation6 uia6 = IUIAutomation6;
            if (uia6 != null)
            {
                uia6.AddActiveTextPositionChangedEventHandler(Element, Scope, null, this);
                IsHooked = true;
            }
        }

        public void HandleActiveTextPositionChangedEvent(IUIAutomationElement sender, IUIAutomationTextRange range)
        {
            if (range == null) return;

#pragma warning disable CA2000 // Call IDisposable.Dispose()
            var m = EventMessage.GetInstance(EventId, sender);
#pragma warning restore CA2000

            if (m != null)
            {
                const int maxTextLengthToInclude = 100;
                m.Properties = new List<KeyValuePair<string, dynamic>>
                {
                    new KeyValuePair<string, dynamic>("Type", range.GetType()),
                    new KeyValuePair<string, dynamic>("Text", range.GetText(maxTextLengthToInclude))
                };

                ListenEventMessage(m);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!DisposedValue)
            {
                if (disposing)
                {
                    if (IsHooked)
                    {
                        IUIAutomation6 uia6 = IUIAutomation6;
                        if (uia6 != null)
                        {
                            uia6.RemoveActiveTextPositionChangedEventHandler(Element, this);
                        }
                    }
                }
            }
            base.Dispose(disposing);
        }
    }
}
