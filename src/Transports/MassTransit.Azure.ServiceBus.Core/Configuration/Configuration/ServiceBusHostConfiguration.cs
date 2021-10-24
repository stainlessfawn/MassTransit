﻿namespace MassTransit.Azure.ServiceBus.Core.Configuration
{
    using System;
    using Configurators;
    using Definition;
    using global::Azure.Messaging.ServiceBus;
    using GreenPipes;
    using MassTransit.Configuration;
    using MassTransit.Configurators;
    using MassTransit.Topology;
    using MassTransit.Topology.EntityNameFormatters;
    using Pipeline;
    using Settings;
    using Topology;
    using Topology.Configurators;
    using Topology.Topologies;
    using Transport;
    using Transports;
    using Util;


    public class ServiceBusHostConfiguration :
        BaseHostConfiguration<IServiceBusEntityEndpointConfiguration, IServiceBusReceiveEndpointConfigurator>,
        IServiceBusHostConfiguration
    {
        readonly IServiceBusBusConfiguration _busConfiguration;
        readonly Recycle<IConnectionContextSupervisor> _connectionContext;
        readonly IServiceBusHostTopology _hostTopology;
        readonly IServiceBusTopologyConfiguration _topologyConfiguration;
        ServiceBusHostSettings _hostSettings;
        IMessageNameFormatter _messageNameFormatter;

        public ServiceBusHostConfiguration(IServiceBusBusConfiguration busConfiguration, IServiceBusTopologyConfiguration topologyConfiguration)
            : base(busConfiguration)
        {
            _busConfiguration = busConfiguration;
            _topologyConfiguration = topologyConfiguration;

            _hostSettings = new HostSettings();
            _hostTopology = new ServiceBusHostTopology(this, _topologyConfiguration);

            ReceiveTransportRetryPolicy = Retry.CreatePolicy(x =>
            {
                x.Ignore<ServiceBusException>(ex => ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound);
                x.Ignore<ServiceBusException>(ex => ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists);
                x.Ignore<ServiceBusException>(ex => ex.Reason == ServiceBusFailureReason.MessageNotFound);
                x.Ignore<ServiceBusException>(ex => ex.Reason == ServiceBusFailureReason.MessageSizeExceeded);

                x.Ignore<UnauthorizedAccessException>();

                x.Handle<ServiceBusException>(exception => exception.Reason == ServiceBusFailureReason.ServiceBusy && exception.IsTransient);
                x.Handle<TimeoutException>();

                x.Interval(5, TimeSpan.FromSeconds(10));
            });

            _connectionContext = new Recycle<IConnectionContextSupervisor>(() => new ConnectionContextSupervisor(this, topologyConfiguration));
        }

        public override Uri HostAddress => _hostSettings.ServiceUri;

        string IServiceBusHostConfiguration.BasePath => _hostSettings.ServiceUri.AbsolutePath.Trim('/');

        public IConnectionContextSupervisor ConnectionContextSupervisor => _connectionContext.Supervisor;

        public ServiceBusHostSettings Settings
        {
            get => _hostSettings;
            set
            {
                _hostSettings = value ?? throw new ArgumentNullException(nameof(value));

                if (_hostSettings.TokenCredential != null)
                    SetNamespaceSeparatorToUnderscore();
            }
        }

        public override IRetryPolicy ReceiveTransportRetryPolicy { get; }

        IServiceBusHostTopology IServiceBusHostConfiguration.HostTopology => _hostTopology;

        public void SetNamespaceSeparatorToTilde()
        {
            _messageNameFormatter = new ServiceBusMessageNameFormatter("~");
            _topologyConfiguration.Message.SetEntityNameFormatter(new MessageNameFormatterEntityNameFormatter(_messageNameFormatter));
        }

        public void SetNamespaceSeparatorToUnderscore()
        {
            _messageNameFormatter = new ServiceBusMessageNameFormatter("_");
            _topologyConfiguration.Message.SetEntityNameFormatter(new MessageNameFormatterEntityNameFormatter(_messageNameFormatter));
        }

        public void SetNamespaceSeparatorTo(string separator)
        {
            _messageNameFormatter = new ServiceBusMessageNameFormatter(separator);
            _topologyConfiguration.Message.SetEntityNameFormatter(new MessageNameFormatterEntityNameFormatter(_messageNameFormatter));
        }

        public override void ReceiveEndpoint(IEndpointDefinition definition, IEndpointNameFormatter endpointNameFormatter,
            Action<IServiceBusReceiveEndpointConfigurator> configureEndpoint = null)
        {
            var queueName = definition.GetEndpointName(endpointNameFormatter ?? DefaultEndpointNameFormatter.Instance);

            ReceiveEndpoint(queueName, configurator =>
            {
                ApplyEndpointDefinition(configurator, definition);
                configureEndpoint?.Invoke(configurator);
            });
        }

        public override void ReceiveEndpoint(string queueName, Action<IServiceBusReceiveEndpointConfigurator> configureEndpoint)
        {
            CreateReceiveEndpointConfiguration(queueName, configureEndpoint);
        }

        public void ApplyEndpointDefinition(IServiceBusReceiveEndpointConfigurator configurator, IEndpointDefinition definition)
        {
            if (definition.IsTemporary)
            {
                configurator.AutoDeleteOnIdle = Defaults.TemporaryAutoDeleteOnIdle;
                configurator.RemoveSubscriptions = true;
            }

            base.ApplyEndpointDefinition(configurator, definition);
        }

        public IServiceBusReceiveEndpointConfiguration CreateReceiveEndpointConfiguration(string queueName,
            Action<IServiceBusReceiveEndpointConfigurator> configure)
        {
            var endpointConfiguration = _busConfiguration.CreateEndpointConfiguration();

            var settings = new ReceiveEndpointSettings(endpointConfiguration, queueName, new QueueConfigurator(queueName));


            return CreateReceiveEndpointConfiguration(settings, endpointConfiguration, configure);
        }

        public IServiceBusReceiveEndpointConfiguration CreateReceiveEndpointConfiguration(ReceiveEndpointSettings settings,
            IServiceBusEndpointConfiguration endpointConfiguration, Action<IServiceBusReceiveEndpointConfigurator> configure)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (endpointConfiguration == null)
                throw new ArgumentNullException(nameof(endpointConfiguration));

            var configuration = new ServiceBusReceiveEndpointConfiguration(this, settings, endpointConfiguration);

            configure?.Invoke(configuration);

            Observers.EndpointConfigured(configuration);

            Add(configuration);

            return configuration;
        }

        public void SubscriptionEndpoint<T>(string subscriptionName, Action<IServiceBusSubscriptionEndpointConfigurator> configure)
            where T : class
        {
            var endpointConfiguration = _busConfiguration.CreateEndpointConfiguration();
            var settings = new SubscriptionEndpointSettings(endpointConfiguration, _busConfiguration.Topology.Publish.GetMessageTopology<T>().TopicDescription,
                subscriptionName);

            CreateSubscriptionEndpointConfiguration(settings, endpointConfiguration, configure);
        }

        public void SubscriptionEndpoint(string subscriptionName, string topicPath, Action<IServiceBusSubscriptionEndpointConfigurator> configure)
        {
            var endpointConfiguration = _busConfiguration.CreateEndpointConfiguration();
            var settings = new SubscriptionEndpointSettings(endpointConfiguration, topicPath, subscriptionName);

            CreateSubscriptionEndpointConfiguration(settings, endpointConfiguration, configure);
        }

        public override IHostTopology HostTopology => _hostTopology;

        public override IReceiveEndpointConfiguration CreateReceiveEndpointConfiguration(string queueName,
            Action<IReceiveEndpointConfigurator> configure = null)
        {
            return CreateReceiveEndpointConfiguration(queueName, configure);
        }

        public override IHost Build()
        {
            var host = new ServiceBusHost(this, _hostTopology);

            foreach (var endpointConfiguration in GetConfiguredEndpoints())
                endpointConfiguration.Build(host);

            return host;
        }

        public IServiceBusSubscriptionEndpointConfiguration CreateSubscriptionEndpointConfiguration<T>(string subscriptionName,
            Action<IServiceBusSubscriptionEndpointConfigurator> configure)
            where T : class
        {
            var endpointConfiguration = _busConfiguration.CreateEndpointConfiguration();
            var settings = new SubscriptionEndpointSettings(endpointConfiguration, _busConfiguration.Topology.Publish.GetMessageTopology<T>().TopicDescription,
                subscriptionName);

            return CreateSubscriptionEndpointConfiguration(settings, endpointConfiguration, configure);
        }

        public IServiceBusSubscriptionEndpointConfiguration CreateSubscriptionEndpointConfiguration(string subscriptionName, string topicPath,
            Action<IServiceBusSubscriptionEndpointConfigurator> configure)
        {
            var endpointConfiguration = _busConfiguration.CreateEndpointConfiguration();
            var settings = new SubscriptionEndpointSettings(endpointConfiguration, topicPath, subscriptionName);

            return CreateSubscriptionEndpointConfiguration(settings, endpointConfiguration, configure);
        }

        public IServiceBusSubscriptionEndpointConfiguration CreateSubscriptionEndpointConfiguration(SubscriptionEndpointSettings settings,
            IServiceBusEndpointConfiguration endpointConfiguration, Action<IServiceBusSubscriptionEndpointConfigurator> configure)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (endpointConfiguration == null)
                throw new ArgumentNullException(nameof(endpointConfiguration));

            var configuration = new ServiceBusSubscriptionEndpointConfiguration(this, settings, endpointConfiguration);

            configure?.Invoke(configuration);

            Observers.EndpointConfigured(configuration);

            Add(configuration);

            return configuration;
        }
    }
}
