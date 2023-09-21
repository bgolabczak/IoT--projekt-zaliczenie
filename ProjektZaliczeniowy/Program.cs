using AzureDeviceSdkDemo.Device;
using Microsoft.Azure.Devices.Client;
using Opc.UaFx;
using Opc.UaFx.Client;
using ProjektZaliczeniowy.Properties;
using Microsoft.Azure.Devices;

internal class Program 
{
    private static string NODE_PRODUCT_STATUS = "/ProductionStatus";
    private static string NODE_WORKORDER_ID = "/WorkorderId";
    private static string NODE_GOOD_COUNT = "/GoodCount";
    private static string NODE_BAD_COUNT = "/BadCount";
    private static string NODE_TEMPERATURE = "/Temperature";
    private static string NODE_DEVICE_ERROR = "/DeviceError";
    private static string NODE_PRODUCT_RATE = "/ProductionRate";
    private static string PARAM_MACHINE_ID = "machine_id";
    private static string PARAM_PRODUCT_STATUS = "product_status";
    private static string PARAM_WORKORDER_ID = "workorder_id";
    private static string PARAM_GOOD_COUNT = "good_count";
    private static string PARAM_BAD_COUNT = "bad_count";
    private static string PARAM_TEMPERATURE = "temperature";
    private static string PARAM_IOT_HUB_DEVICE_ID = "deviceId";
    private static string PARAM_DEVICE_ERROR = "device_error";
    private static string PARAM_PRODUCT_RATE = "product_rate";
    private static string STR_DEVICE = "Device";
    private static string STR_IOT_DEVICE_TEMP = "device_";
    private static List<string> LIST_TELEMETRY_PARAMS = new List<string>
    {
        PARAM_MACHINE_ID,
        PARAM_IOT_HUB_DEVICE_ID,
        PARAM_PRODUCT_STATUS,
        PARAM_WORKORDER_ID,
        PARAM_GOOD_COUNT,
        PARAM_BAD_COUNT,
        PARAM_TEMPERATURE
    };

    [Obsolete]
    private static async Task Main(string[] args)  
    {
        try
        {
            List<MachineData> machineDataList = new List<MachineData>();
            int errorCodeBefore = 0;
            using (var opcClient = new OpcClient(Resources.opcClientServer))
            {
                List<string> deviceIds = new List<string>();

                //Opc client connection and nodes reading
                opcClient.Connect();
                var node = opcClient.BrowseNode(OpcObjectTypes.ObjectsFolder);
                findMachinesId(node, machineDataList);

                //Read the devices available for connection in the IoT Hub
                RegistryManager registryManager = RegistryManager.CreateFromConnectionString(Resources.ownerConnectionString);
                IEnumerable<Device> devices = await registryManager.GetDevicesAsync(int.Parse(Resources.iotDevicesMaxCount));
                foreach (Device device in devices)
                {
                    if (device.Id.Contains(STR_IOT_DEVICE_TEMP))
                    {
                        deviceIds.Add(device.Id);
                    }
                }

                //Сonnecting devices on the Opc server and in the IoT Hub
                Dictionary<VirtualDevice, MachineData> iotHubDevice2OpcMachineData = new Dictionary<VirtualDevice, MachineData>();
                for(int i = 0; i < deviceIds.Count; i++)
                {
                    if (i < machineDataList.Count)
                    {
                        DeviceClient deviceClient = DeviceClient.CreateFromConnectionString(Resources.deviceConnectionString, deviceIds[i]);
                        await deviceClient.OpenAsync();
                        var device = new VirtualDevice(deviceClient, opcClient);
                        await device.InitializeHandlers(machineDataList[i].machineId);
                        iotHubDevice2OpcMachineData.Add(device, machineDataList[i]);
                        machineDataList[i].iotHubDeviceId = deviceIds[i];
                    }
                    
                }

                //Read data from Opc Client Nodes and Set Device Twins
                foreach (KeyValuePair<VirtualDevice, MachineData> kvp in iotHubDevice2OpcMachineData)
                {
                    readNode(kvp.Value, opcClient);
                    Console.WriteLine($"{DateTime.Now.ToLocalTime()}> Program.Main()> Connected device: {kvp.Value.workorderId}");
                    await kvp.Key.SetTwinAsync(kvp.Value.deviceErrors, kvp.Value.productRate);
                    if (kvp.Value.deviceErrors > 0)
                    {
                        reportNewError(kvp.Key, kvp.Value.deviceErrors, kvp.Value.machineId, kvp.Value.iotHubDeviceId);
                    }
                }

                //Reading Data from Opc client every 5 sec
                while (true)
                {
                    foreach (KeyValuePair<VirtualDevice, MachineData> kvp in iotHubDevice2OpcMachineData)
                    {
                        errorCodeBefore = kvp.Value.deviceErrors;
                        readNode(kvp.Value, opcClient);
                        if(kvp.Value.productStatus == 1)
                        {
                            await kvp.Key.SendMessages(filterTelemetry2Send(kvp.Value, LIST_TELEMETRY_PARAMS));
                            if (kvp.Value.deviceErrors > 0 && errorCodeBefore != kvp.Value.deviceErrors)
                            {
                                reportNewError(kvp.Key, kvp.Value.deviceErrors, kvp.Value.machineId, kvp.Value.iotHubDeviceId);
                            }
                            
                        }
                    }
                    await Task.Delay(5000);
                }
            }
        }
        catch (OpcException e)
        {
            Console.WriteLine($"{DateTime.Now.ToLocalTime()}> Program.Main()> Connection failed: {e.Message}");
        }

        static void findMachinesId(OpcNodeInfo node, List<MachineData> machineDataList, int level = 0)
        {
            if (level == 1 && node.NodeId.ToString().Contains(STR_DEVICE))
            {
                machineDataList.Add(new MachineData
                {
                    machineId = node.NodeId.ToString()
                });
            }
            level++;
            foreach (var childNode in node.Children())
            {
                findMachinesId(childNode, machineDataList, level);
            }
        }

        static void readNode(MachineData machineData, OpcClient client)
        {
            machineData.productStatus = (int)client.ReadNode(machineData.machineId + NODE_PRODUCT_STATUS).Value;
            machineData.workorderId = (string)client.ReadNode(machineData.machineId + NODE_WORKORDER_ID).Value;
            machineData.goodCount = (int)(long)client.ReadNode(machineData.machineId + NODE_GOOD_COUNT).Value;
            machineData.badCount = (int)(long)client.ReadNode(machineData.machineId + NODE_BAD_COUNT).Value;
            machineData.temperature = (double)client.ReadNode(machineData.machineId + NODE_TEMPERATURE).Value;
            machineData.deviceErrors = (int)client.ReadNode(machineData.machineId + NODE_DEVICE_ERROR).Value;
            machineData.productRate = (int)client.ReadNode(machineData.machineId + NODE_PRODUCT_RATE).Value;
            machineData.readTimeStamp = DateTime.Now;
        }

        static string filterTelemetry2Send(MachineData machineData, List<string> requiredParamsList)
        {
            string data = "{";
            foreach (string param in requiredParamsList)
            {
                machineData.addParamAndValue2Json(ref data,param);
            }
            return (data.Remove(data.Length - 1, 1) + "}");
        }

        static async void reportNewError(VirtualDevice device, int errorCode, string machineId, string deviceId)
        {
            await device.SendMessages(prepareErrorsJson(errorCode, machineId, deviceId));
            await device.UpdateTwinAsync(errorCode);
        }

        static string prepareErrorsJson(int errorCode, string machineId, string deviceId)
        {
            if (errorCode < 1) return null;
            string binaryString = Convert.ToString(errorCode, 2).PadLeft(4, '0');
            char on = '1';
            string ret = "{";
            ret += "\"machine_id\":\"" + machineId + "\",";
            ret += "\"deviceId\":\"" + deviceId + "\",";
            ret += "\"isError\":true,";

            if (binaryString[0] == on)
            {
                ret += "\"unknown\":true,";
            }
            if(binaryString[1] == on)
            {
                ret += "\"sensor_failure\":true,";
            }
            if(binaryString[2] == on)
            {
                ret += "\"power_failure\":true,";
            }
            if (binaryString[3] == on)
            {
                ret += "\"emergency_stop\":true,";
            }

            return (ret.Remove(ret.Length - 1, 1) + "}");
        }
    }

    public class MachineData
    {
        public string iotHubDeviceId { get; set; }
        public string machineId { get; set; }
        public int productStatus { get; set; }
        public string workorderId { get; set; }
        public int goodCount { get; set; }
        public int badCount { get; set; }
        public double temperature { get; set; }
        public int deviceErrors { get; set; }
        public int productRate { get; set; }
        public DateTime readTimeStamp { get; set; }

        public void addParamAndValue2Json(ref string data, string paramName)
        {
            data += "\"" + paramName + "\":";
            if (paramName == "machine_id")
                data += "\"" + machineId + "\"";
            if (paramName == "product_status")
                data += productStatus;
            if (paramName == "workorder_id")
                data += "\"" + workorderId + "\"";
            if (paramName == "good_count")
                data += goodCount;
            if (paramName == "bad_count")
                data += badCount;
            if (paramName == "temperature")
                data += temperature.ToString().Replace(',','.');
            if (paramName == "device_error")
                data += deviceErrors;
            if (paramName == "product_rate")
                data += productRate;
            if (paramName == "read_time_stamp")
                data += "\"" + readTimeStamp + "\"";
            if (paramName == "deviceId")
                data += "\"" + iotHubDeviceId + "\"";
            data += ",";
        }
    }
}