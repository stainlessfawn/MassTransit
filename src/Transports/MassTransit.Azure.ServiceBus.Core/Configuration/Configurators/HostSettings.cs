namespace MassTransit.Azure.ServiceBus.Core.Configurators
{
    using System;
    using global::Azure;
    using global::Azure.Core;
    using global::Azure.Messaging.ServiceBus;


    public class HostSettings :
        ServiceBusHostSettings
    {
        public HostSettings()
        {
            RetryMinBackoff = TimeSpan.FromMilliseconds(100);
            RetryMaxBackoff = TimeSpan.FromSeconds(30);
            RetryLimit = 10;

            TransportType = ServiceBusTransportType.AmqpTcp;

            ServiceUri = new Uri("sb://no-host-configured");
        }

        public Uri ServiceUri { get; set; }
        public string ConnectionString { get; set; }
        public AzureNamedKeyCredential NamedKeyCredential { get; set; }
        public AzureSasCredential SasCredential { get; set; }
        public TokenCredential TokenCredential { get; set; }
        public TimeSpan RetryMinBackoff { get; set; }
        public TimeSpan RetryMaxBackoff { get; set; }
        public int RetryLimit { get; set; }
        public ServiceBusTransportType TransportType { get; set; }
    }
}
