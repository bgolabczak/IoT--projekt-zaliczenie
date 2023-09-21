using System;
using Microsoft.Azure.Devices;
using System.Text;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs.ServiceBus;
using System.Threading.Tasks;

namespace D2CErrorMessages
{
    public class FunctionServiceBusDecreaseRateQueue
    {
        [FunctionName("FunctionServiceBusDecreaseRateQueue")]
        public async Task RunAsync([ServiceBusTrigger("%ServiceBusDecreaseRateQueue%", Connection = "ServiceBusConnectionString")] ServiceBusReceivedMessage message, ServiceBusMessageActions messageActions, ILogger log, ExecutionContext context)
        {
            var myQueueItem = Encoding.UTF8.GetString(message.Body);
            log.LogInformation($"C# ServiceBus queue trigger function processed message: {myQueueItem}");

            ReceivedMessage mesg = JsonConvert.DeserializeObject<ReceivedMessage>(myQueueItem);

            Console.WriteLine(mesg.time);
            Console.WriteLine(mesg.deviceId);

            string connectionString = "HostName=iot-dhyrenko-ul-standard.azure-devices.net;SharedAccessKeyName=iothubowner;SharedAccessKey=yYImzJ2fC/yqrln2IjEL5/37/e9W7EIORtnNzaljE+k=";
            string deviceId = mesg.deviceId;
            string methodName = "DecreaseProductRate";

            ServiceClient serviceClient = ServiceClient.CreateFromConnectionString(connectionString);

            CloudToDeviceMethod method = new CloudToDeviceMethod(methodName);
            method.ResponseTimeout = TimeSpan.FromSeconds(30);
            CloudToDeviceMethodResult response = await serviceClient.InvokeDeviceMethodAsync(deviceId, method);
            Console.WriteLine(response.Status);
            Console.WriteLine(response.GetPayloadAsJson());
        }

        public class ReceivedMessage
        {
            public DateTime time { get; set; }
            public string deviceId { get; set; }
        }
    }
}
