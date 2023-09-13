////
// Copyright (c) .NET Foundation and Contributors.
// See LICENSE file in the project root for full license information.
////

using CommunityToolkit.Mvvm.Messaging.Messages;

namespace nanoFramework.Tools.NanoProfiler.ViewModels
{
    public class UpdateLogTextMessage : ValueChangedMessage<string>
    {
        public UpdateLogTextMessage(string value) : base(value)
        {
        }
    }

}
