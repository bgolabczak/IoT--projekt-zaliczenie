using System;
using System.Text;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.ServiceBus;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.Devices;
using System.Threading.Tasks;

namespace D2CErrorMessages
{
    public class FunctionServiceBusErrorQueue
    {
        [FunctionName("FunctionServiceBusErrorQueue")]
        public async Task RunAsync([ServiceBusTrigger("%ServiceBusErrorQueue%", Connection = "ServiceBusConnectionString")] ServiceBusReceivedMessage message, ServiceBusMessageActions messageActions, ILogger log, ExecutionContext context)
        {
            var myQueueItem = Encoding.UTF8.GetString(message.Body);
            log.LogInformation($"C# ServiceBus queue trigger function processed message: {myQueueItem}");

            ReceivedMessage mesg = JsonConvert.DeserializeObject<ReceivedMessage>(myQueueItem);

            string connectionString = "HostName=iot-dhyrenko-ul-standard.azure-devices.net;SharedAccessKeyName=iothubowner;SharedAccessKey=yYImzJ2fC/yqrln2IjEL5/37/e9W7EIORtnNzaljE+k=";
            string deviceId = mesg.deviceId;
            string methodName = "EmergencyStop";

            ServiceClient serviceClient = ServiceClient.CreateFromConnectionString(connectionString);

            CloudToDeviceMethod method = new CloudToDeviceMethod(methodName);
            method.ResponseTimeout = TimeSpan.FromSeconds(30);
            CloudToDeviceMethodResult response = await serviceClient.InvokeDeviceMethodAsync(deviceId, method);
            Console.WriteLine(response.Status);
            Console.WriteLine(response.GetPayloadAsJson());
        }

        public class ReceivedMessage
        {
            public DateTime emergency_stop_time { get; set; }
            public string deviceId { get; set; }
            public double error_sum { get; set; }
        }
    }
}
