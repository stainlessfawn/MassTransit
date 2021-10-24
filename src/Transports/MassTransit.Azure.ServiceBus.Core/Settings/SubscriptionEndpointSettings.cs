namespace MassTransit.Azure.ServiceBus.Core.Settings
{
    using System;
    using System.Collections.Generic;
    using Configuration;
    using global::Azure.Messaging.ServiceBus.Administration;
    using Topology;
    using Topology.Configurators;
    using Transport;


    public class SubscriptionEndpointSettings :
        BaseClientSettings,
        SubscriptionSettings
    {
        readonly SubscriptionConfigurator _subscriptionConfigurator;
        readonly CreateTopicOptions _topicDescription;

        public SubscriptionEndpointSettings(IServiceBusEndpointConfiguration configuration, string topicName, string subscriptionName)
            : this(configuration, Defaults.CreateTopicDescription(topicName), subscriptionName)
        {
        }

        public SubscriptionEndpointSettings(IServiceBusEndpointConfiguration configuration, CreateTopicOptions topicDescription, string subscriptionName)
            : this(configuration, topicDescription, new SubscriptionConfigurator(topicDescription.Name, subscriptionName))
        {
        }

        SubscriptionEndpointSettings(IServiceBusEndpointConfiguration configuration, CreateTopicOptions topicDescription, SubscriptionConfigurator configurator)
            : base(configuration, configurator)
        {
            _topicDescription = topicDescription;
            _subscriptionConfigurator = configurator;

            Name = Path = EntityNameFormatter.FormatSubscriptionPath(_subscriptionConfigurator.TopicPath, _subscriptionConfigurator.SubscriptionName);
        }

        public ISubscriptionConfigurator SubscriptionConfigurator => _subscriptionConfigurator;

        CreateTopicOptions SubscriptionSettings.TopicDescription => _topicDescription;
        CreateSubscriptionOptions SubscriptionSettings.SubscriptionDescription => _subscriptionConfigurator.GetSubscriptionDescription();

        public CreateRuleOptions Rule { get; set; }
        public RuleFilter Filter { get; set; }

        public override TimeSpan LockDuration => _subscriptionConfigurator.LockDuration ?? Defaults.LockDuration;

        public override string Path { get; }

        public override bool RequiresSession => _subscriptionConfigurator.RequiresSession ?? false;

        public bool RemoveSubscriptions { get; set; }

        protected override IEnumerable<string> GetQueryStringOptions()
        {
            if (_subscriptionConfigurator.AutoDeleteOnIdle.HasValue && _subscriptionConfigurator.AutoDeleteOnIdle.Value > TimeSpan.Zero
                && _subscriptionConfigurator.AutoDeleteOnIdle.Value != Defaults.AutoDeleteOnIdle)
                yield return $"autodelete={_subscriptionConfigurator.AutoDeleteOnIdle.Value.TotalSeconds}";
        }

        public override void SelectBasicTier()
        {
            _subscriptionConfigurator.AutoDeleteOnIdle = default;
            _subscriptionConfigurator.DefaultMessageTimeToLive = Defaults.BasicMessageTimeToLive;
        }
    }
}
