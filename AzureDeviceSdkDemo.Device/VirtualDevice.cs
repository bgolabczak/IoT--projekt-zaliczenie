using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;
using System.Net.Mime;
using System.Text;
using Opc.UaFx;
using Opc.UaFx.Client;
using Microsoft.Azure.Devices.Shared;

namespace AzureDeviceSdkDemo.Device
{
    public class VirtualDevice
    {

        private readonly DeviceClient client;
        private readonly OpcClient opcClient;

        public VirtualDevice(DeviceClient client, OpcClient opcClient)
        {
            this.client = client;
            this.opcClient = opcClient;
        }
        #region Sending Messages
        public async Task SendMessages(string data)
        {
            if(data != null)
            {
                Message eventMessage = new Message(Encoding.UTF8.GetBytes(data));
                eventMessage.ContentType = MediaTypeNames.Application.Json;
                eventMessage.ContentEncoding = "utf-8";
                Console.WriteLine($"\t{DateTime.Now.ToLocalTime()}> D2C Sending message: {data}");

                await client.SendEventAsync(eventMessage);
                Console.WriteLine();
            }
        }
        #endregion Sending Messages
        #region Receiving Messages
        private async Task OnC2dMessageReceivedAsync(Message receivedMessage, object _)
        {
            Console.WriteLine($"\t{DateTime.Now}> C2D message callback - message received with Id={receivedMessage.MessageId}.");
            PrintMessage(receivedMessage);

            await client.CompleteAsync(receivedMessage);
            Console.WriteLine($"\t{DateTime.Now}> Completed C2D message with Id={receivedMessage.MessageId}.");

            receivedMessage.Dispose();
        }

        private void PrintMessage(Message receivedMessage)
        {
            string messageData = Encoding.ASCII.GetString(receivedMessage.GetBytes());
            Console.WriteLine($"\t\tReceived message: {messageData}");

            int propCount = 0;
            foreach (var prop in receivedMessage.Properties)
            {
                Console.WriteLine($"\t\tProperty[{propCount++}> Key={prop.Key} : Value={prop.Value}");
            }
        }

        #endregion Receiving Messages
        #region Device Twin
        public async Task SetTwinAsync(int deviceError, int prodRate)
        {
            var twin = await client.GetTwinAsync();
            Console.WriteLine();

            var reportedProperties = new TwinCollection();
            reportedProperties["DeviceErrors"] = deviceError;
            reportedProperties["ProductionRate"] = prodRate;
            if (deviceError != 0)
            {
                reportedProperties["LastErrorDate"] = DateTime.Now;
            }
            
            await client.UpdateReportedPropertiesAsync(reportedProperties);
            Console.WriteLine($"{DateTime.Now}> Device Twin value was set.");

        }

        public async Task UpdateTwinAsync(int deviceError)
        {
            var twin = await client.GetTwinAsync();
            Console.WriteLine($"{DateTime.Now}> Device Twin value was update.");
            Console.WriteLine();

            var reportedProperties = new TwinCollection();
            reportedProperties["DeviceErrors"] = deviceError;
            reportedProperties["LastErrorDate"] = DateTime.Now;

            await client.UpdateReportedPropertiesAsync(reportedProperties);
        }

        private async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
        {
            Console.WriteLine($"\t{DateTime.Now}> Device Twin. Desired property change:\n\t{JsonConvert.SerializeObject(desiredProperties)}");
            string nodeId = (string)userContext;
            int newProdRate = desiredProperties["ProductionRate"];
            string node = nodeId + "/ProductionRate";

            OpcStatus result = opcClient.WriteNode(node, newProdRate);
            Console.WriteLine($"\t{DateTime.Now}> opcClient.WriteNode is result good: " + result.IsGood.ToString());
            TwinCollection reportedProperties = new TwinCollection();
            reportedProperties["DateTimeLastDesiredPropertyChangeReceived"] = DateTime.Now;
            reportedProperties["ProductionRate"] = desiredProperties["ProductionRate"];

            await client.UpdateReportedPropertiesAsync(reportedProperties).ConfigureAwait(false);
        }
        #endregion Device Twin
        #region Direct Methods

        private async Task<MethodResponse> EmergencyStopHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"\t{DateTime.Now}> METHOD EXECUTED: {methodRequest.Name}");
            string nodeId = (string)userContext;
            object[] result = opcClient.CallMethod(
                    nodeId,
                    nodeId + "/EmergencyStop"
                    );
            return new MethodResponse(0);
        }

        private async Task<MethodResponse> ResetErrorStatusHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"\t{DateTime.Now}> METHOD EXECUTED: {methodRequest.Name}");
            string nodeId = (string)userContext;
            object[] result = opcClient.CallMethod(
                    nodeId,
                    nodeId + "/ResetErrorStatus"
                    );
            return new MethodResponse(0);
        }

        private async Task<MethodResponse> DecreaseProductRateHandler(MethodRequest methodRequest, object userContext)
        {
            string productionRate = "/ProductionRate";
            string deviceError = "/DeviceError";
            Console.WriteLine($"\t{DateTime.Now}> METHOD EXECUTED: {methodRequest.Name}");
            string nodeId = (string)userContext;
            int rate = (int)opcClient.ReadNode(nodeId + productionRate).Value;
            int error = (int)opcClient.ReadNode(nodeId + deviceError).Value;
            OpcStatus result = opcClient.WriteNode(nodeId + productionRate, rate - 10);
            Console.WriteLine(result.ToString());
            await SetTwinAsync(error, rate - 10);
            return new MethodResponse(0);
        }

        private async Task<MethodResponse> MaintenanceDoneHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"\t{DateTime.Now}> METHOD EXECUTED: {methodRequest.Name}");

            var twin = await client.GetTwinAsync();
            
            var reportedProperties = new TwinCollection();
            reportedProperties["LastMainTenanceDone"] = DateTime.Now;

            await client.UpdateReportedPropertiesAsync(reportedProperties);

            Console.WriteLine($"\n{DateTime.Now}> Device Twin Maintenance Done.");
            Console.WriteLine();

            return new MethodResponse(0);
        }

        private static async Task<MethodResponse> DefaultServiceHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"\t{DateTime.Now}> METHOD NOT EXIST: {methodRequest.Name}");
            await Task.Delay(1000);
            return new MethodResponse(0);
        }
        #endregion Direct Methods
        public async Task InitializeHandlers(string userContext)
        {
            await client.SetReceiveMessageHandlerAsync(OnC2dMessageReceivedAsync, userContext);

            await client.SetMethodHandlerAsync("EmergencyStop", EmergencyStopHandler, userContext);
            await client.SetMethodHandlerAsync("ResetErrorStatus", ResetErrorStatusHandler, userContext);
            await client.SetMethodHandlerAsync("DecreaseProductRate", DecreaseProductRateHandler, userContext);
            await client.SetMethodHandlerAsync("MaintenanceDone", MaintenanceDoneHandler, userContext);
            await client.SetMethodDefaultHandlerAsync(DefaultServiceHandler, userContext);

            await client.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChanged, userContext);
        }
    }
}