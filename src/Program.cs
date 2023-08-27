// Copyright (c) Microsoft. All rights reserved.
using Microsoft.Extensions.Logging;

namespace LinuxTemperatureSensor
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Net;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using Microsoft.Azure.Devices.Edge.Util;
    using Microsoft.Azure.Devices.Edge.Util.Concurrency;
    using Microsoft.Azure.Devices.Edge.Util.TransientFaultHandling;
    using Microsoft.Azure.Devices.Shared;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using ExponentialBackoff = Microsoft.Azure.Devices.Edge.Util.TransientFaultHandling.ExponentialBackoff;


    class Program
    {
        const string SendDataConfigKey = "SendData";
        const string SendIntervalConfigKey = "Period";

        const string InputMessageHandlerName = "control";
        const string EventoutputName = "temperatureOutput";

        private static ILogger logger = null;




        static readonly ITransientErrorDetectionStrategy DefaultTimeoutErrorDetectionStrategy =
            new DelegateErrorDetectionStrategy(ex => ex.HasTimeoutException());

        static readonly RetryStrategy DefaultTransientRetryStrategy =
            new ExponentialBackoff(
                5,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(60),
                TimeSpan.FromSeconds(4));

        static readonly Guid BatchId = Guid.NewGuid();
        static readonly AtomicBoolean Reset = new AtomicBoolean(false);
        static TimeSpan messageDelay;
        static bool sendData = true;

        public static int Main() => MainAsync().Result;
        static async Task<int> MainAsync()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("config/appsettings.json", optional: false)
                .AddEnvironmentVariables()
                .Build();

           var loggerFactory = LoggerFactory.Create(builder =>
                {
                    builder
                        //                    .AddFilter("Microsoft", LogLevel.Warning)
                        //                    .AddFilter("System", LogLevel.Warning)
                        //                    .AddFilter("Program", LogLevel.Debug)
                        .AddConfiguration(configuration.GetSection("Logging"))
                        .AddConsole();

                });


            logger = loggerFactory.CreateLogger<Program>();

            logger.LogInformation("LinuxTemperatureSensor Main() started.");

            messageDelay = TimeSpan.FromSeconds(configuration.GetValue(SendIntervalConfigKey, 900));
                
            logger.LogInformation(
                $"Initializing temperature sensor to send "
                + $"messages, at an interval of {messageDelay.TotalSeconds} seconds.");

            TransportType transportType = configuration.GetValue("ClientTransportType", TransportType.Amqp_Tcp_Only);

            ModuleClient moduleClient = await CreateModuleClientAsync(
                transportType,
                DefaultTimeoutErrorDetectionStrategy,
                DefaultTransientRetryStrategy);
            await moduleClient.OpenAsync();
            await moduleClient.SetMethodHandlerAsync("reset", ResetMethodAsync, null);

            (CancellationTokenSource cts, ManualResetEventSlim completed, Option<object> handler) = ShutdownHandler.Init(TimeSpan.FromSeconds(5), null);

            Twin currentTwinProperties = await moduleClient.GetTwinAsync();
            if (currentTwinProperties.Properties.Desired.Contains(SendIntervalConfigKey))
            {
                messageDelay = TimeSpan.FromSeconds((int)currentTwinProperties.Properties.Desired[SendIntervalConfigKey]);
            }

            if (currentTwinProperties.Properties.Desired.Contains(SendDataConfigKey))
            {
                sendData = (bool)currentTwinProperties.Properties.Desired[SendDataConfigKey];
                if (!sendData)
                {
                    logger.LogInformation("Sending data disabled. Change twin configuration to start sending again.");
                }
            }

            ModuleClient userContext = moduleClient;
            await moduleClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertiesUpdatedAsync, userContext);
            await moduleClient.SetInputMessageHandlerAsync(InputMessageHandlerName, ControlMessageHandleAsync, userContext);
            await SendEventsAsync(moduleClient, cts);
            await cts.Token.WhenCanceled();

            completed.Set();
            handler.ForEach(h => GC.KeepAlive(h));

            logger.LogInformation("LinuxTemperatureSensor Main() finished.");
            return 0;
        }

        // Control Message expected to be:
        // {
        //     "period" : "seconds"
        // }
        static async Task<MessageResponse> ControlMessageHandleAsync(Message message, object userContext)
        {
            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);

            logger.LogDebug($"Received message Body: [{messageString}]");

            try
            {
                var messages = JsonConvert.DeserializeObject<ControlCommand[]>(messageString);

                foreach (ControlCommand messageBody in messages)
                {
                    messageDelay = new TimeSpan(0, 0, messageBody.Period);
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Error: Failed to deserialize control command with exception: [{ex}]");
            }

            return await Task.FromResult(MessageResponse.Completed);
        }

        static async Task<MethodResponse> ResetMethodAsync(MethodRequest methodRequest, object userContext)
        {
            logger.LogInformation("Received direct method call to reset temperature sensor...");
            Reset.Set(true);
            var response = new MethodResponse((int)HttpStatusCode.OK);
            return await Task.FromResult(response);
        }

        /// <summary>
        /// Module behavior:
        ///        Sends data periodically (with default frequency of 5 seconds).
        ///        Data trend:
        ///         - Machine Temperature regularly rises from 21C to 100C in regularly with jitter
        ///         - Machine Pressure correlates with Temperature 1 to 10psi
        ///         - Ambient temperature stable around 21C
        ///         - Humidity is stable with tiny jitter around 25%
        ///                Method for resetting the data stream.
        /// </summary>
        static async Task SendEventsAsync(
            ModuleClient moduleClient,
            CancellationTokenSource cts)
        {
            int count = 1;

            while (!cts.Token.IsCancellationRequested) { 
            
                logger.LogDebug("Calling Sensors -j (2)");

                ProcessStartInfo startInfo = new ProcessStartInfo() { FileName = "sensors", Arguments = "-j", RedirectStandardInput = true, RedirectStandardOutput=true};
                Process sensors = new Process() { StartInfo = startInfo };
                sensors.Start();
                await sensors.WaitForExitAsync();

                logger.LogDebug("Sensors -j called");

                var eventMessage = new Message(sensors.StandardOutput.BaseStream);
                eventMessage.ContentEncoding = "utf-8";
                eventMessage.ContentType = "application/json";
                eventMessage.Properties.Add("sequenceNumber", count.ToString());
                eventMessage.Properties.Add("batchId", BatchId.ToString());

                logger.LogDebug($"\t{DateTime.Now.ToLocalTime()}> Sending message: {eventMessage.ToString()}");

                moduleClient.ConfigureAwait(false);
                await moduleClient.SendEventAsync(EventoutputName, eventMessage);

                await Task.Delay(messageDelay, cts.Token);
            }
        }

        static async Task OnDesiredPropertiesUpdatedAsync(TwinCollection desiredPropertiesPatch, object userContext)
        {
            // At this point just update the configure configuration.
            if (desiredPropertiesPatch.Contains(SendIntervalConfigKey))
            {
                messageDelay = TimeSpan.FromSeconds((int)desiredPropertiesPatch[SendIntervalConfigKey]);
            }

            if (desiredPropertiesPatch.Contains(SendDataConfigKey))
            {
                bool desiredSendDataValue = (bool)desiredPropertiesPatch[SendDataConfigKey];
                if (desiredSendDataValue != sendData && !desiredSendDataValue)
                {
                    logger.LogInformation("Sending data disabled. Change twin configuration to start sending again.");
                }

                sendData = desiredSendDataValue;
            }

            var moduleClient = (ModuleClient)userContext;
            var patch = new TwinCollection($"{{ \"SendData\":{sendData.ToString().ToLower()}, \"SendInterval\": {messageDelay.TotalSeconds}}}");
            await moduleClient.UpdateReportedPropertiesAsync(patch); // Just report back last desired property.
        }

        static async Task<ModuleClient> CreateModuleClientAsync(
            TransportType transportType,
            ITransientErrorDetectionStrategy transientErrorDetectionStrategy = null,
            RetryStrategy retryStrategy = null)
        {
            var retryPolicy = new RetryPolicy(transientErrorDetectionStrategy, retryStrategy);
            retryPolicy.Retrying += (_, args) => { logger.LogError($"Retry {args.CurrentRetryCount} times to create module client and failed with exception:{Environment.NewLine}{args.LastException}"); };

            ModuleClient client = await retryPolicy.ExecuteAsync(
                async () =>
                {
                    ITransportSettings[] GetTransportSettings()
                    {
                        switch (transportType)
                        {
                            case TransportType.Mqtt:
                            case TransportType.Mqtt_Tcp_Only:
                                return new ITransportSettings[] { new MqttTransportSettings(TransportType.Mqtt_Tcp_Only) };
                            case TransportType.Mqtt_WebSocket_Only:
                                return new ITransportSettings[] { new MqttTransportSettings(TransportType.Mqtt_WebSocket_Only) };
                            case TransportType.Amqp_WebSocket_Only:
                                return new ITransportSettings[] { new AmqpTransportSettings(TransportType.Amqp_WebSocket_Only) };
                            default:
                                return new ITransportSettings[] { new AmqpTransportSettings(TransportType.Amqp_Tcp_Only) };
                        }
                    }

                    ITransportSettings[] settings = GetTransportSettings();
                    logger.LogInformation($"Trying to initialize module client using transport type [{transportType}].");
                    ModuleClient moduleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
                    await moduleClient.OpenAsync();

                    logger.LogInformation($"Successfully initialized module client of transport type [{transportType}].");
                    return moduleClient;
                });

            return client;
        }

        class ControlCommand
        {
            /// <summary>
            /// The number of seconds between events
            /// </summary>
            [JsonProperty("period")]
            public int Period { get; set; }
        }

    }
}
