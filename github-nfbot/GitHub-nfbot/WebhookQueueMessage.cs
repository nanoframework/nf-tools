//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.GitHub
{
    /// <summary>
    /// Message queued for each received webhook delivery. The payload itself is stored in a blob as it can exceed the queue message size limit.
    /// </summary>
    public class WebhookQueueMessage
    {
        public string DeliveryId { get; set; }

        public string EventName { get; set; }

        public string BlobName { get; set; }
    }
}
