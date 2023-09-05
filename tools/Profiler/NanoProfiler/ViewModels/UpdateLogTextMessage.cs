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
